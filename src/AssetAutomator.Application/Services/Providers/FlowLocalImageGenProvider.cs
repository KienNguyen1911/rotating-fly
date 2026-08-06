using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services.Providers
{
    /// <summary>
    /// Concrete Strategy for Flow Image Local API (OpenAI-Compatible :8787/v1).
    /// Optimized for 1-to-N reference_media_id workflow per gg-flow-integration-api.md documentation.
    /// Reuses reference_media_id via JSON POST /v1/images/generations instead of re-uploading binary image data.
    /// </summary>
    public class FlowLocalImageGenProvider : IImageGenProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        private static readonly SemaphoreSlim _mediaIdLock = new SemaphoreSlim(1, 1);
        private static readonly Dictionary<string, string> _uploadedReferenceMediaIds = new Dictionary<string, string>();

        public string ProviderKey => "flow_local";

        /// <summary>
        /// Clears cached reference_media_ids. Call before starting a new batch.
        /// </summary>
        public static void ClearReferenceMediaCache()
        {
            lock (_uploadedReferenceMediaIds)
            {
                _uploadedReferenceMediaIds.Clear();
            }
        }

        public async Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag, string filePath)> referenceImagesWithFilePath,
            string outputDirectory)
        {
            await ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImagesWithFilePath.ConvertAll(r => (r.base64Data, r.tag)), outputDirectory, referenceImagesWithFilePath);
        }

        public async Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory)
        {
            await ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImages, outputDirectory, null);
        }

        private async Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory,
            List<(string base64Data, string tag, string filePath)>? refWithFilePath)
        {
            // Capture the originating sync context so all BatchImageItem property
            // updates marshal back to the UI thread. WinUI bindings raise
            // PropertyChanged from the dispatcher thread; setting these from a
            // thread-pool thread causes RPC_E_WRONG_THREAD crashes.
            var uiContext = SynchronizationContext.Current;

            void SetItemProperty(Action set)
            {
                if (uiContext != null && SynchronizationContext.Current != uiContext)
                {
                    uiContext.Post(_ => set(), null);
                }
                else
                {
                    set();
                }
            }

            SetItemProperty(() => { item.StartedAt = DateTime.Now; item.Status = "Processing"; });

            string baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:8787/v1" : serverUrl.TrimEnd('/');
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && !baseUrl.Contains("/v1/"))
            {
                baseUrl += "/v1";
            }

            string effApiKey = string.IsNullOrWhiteSpace(apiKey) ? "flow-local-key" : apiKey.Trim();
            string size = MapSize(item.AspectRatio);
            string quality = MapQuality(item.Upscale);
            string model = FormatModelName(item.Model, item.AspectRatio);

            try
            {
                string? effectiveRefMediaId = item.ReferenceMediaId;

                // Step 1: Handle Reference Image upload / caching if not yet provided
                if (string.IsNullOrEmpty(effectiveRefMediaId) &&
                    ((refWithFilePath != null && refWithFilePath.Count > 0) || (referenceImages != null && referenceImages.Count > 0)))
                {
                    string cacheKey = refWithFilePath != null && refWithFilePath.Count > 0
                        ? refWithFilePath[0].filePath
                        : (referenceImages != null && referenceImages.Count > 0 ? referenceImages[0].base64Data : string.Empty);

                    if (cacheKey.Length > 200) cacheKey = cacheKey.Substring(0, 200);

                    lock (_uploadedReferenceMediaIds)
                    {
                        if (!string.IsNullOrEmpty(cacheKey) && _uploadedReferenceMediaIds.TryGetValue(cacheKey, out var cachedId))
                        {
                            effectiveRefMediaId = cachedId;
                        }
                    }

                    if (string.IsNullOrEmpty(effectiveRefMediaId))
                    {
                        await _mediaIdLock.WaitAsync();
                        try
                        {
                            // Double-check cache after acquiring lock
                            lock (_uploadedReferenceMediaIds)
                            {
                                if (!string.IsNullOrEmpty(cacheKey) && _uploadedReferenceMediaIds.TryGetValue(cacheKey, out var cachedId))
                                {
                                    effectiveRefMediaId = cachedId;
                                }
                            }

                            if (string.IsNullOrEmpty(effectiveRefMediaId))
                            {
                                // Upload reference via /v1/images/edits. The response contains BOTH
                                // the initial generated image AND the media_id. By saving that image
                                // for the first item we eliminate the duplicate that would otherwise
                                // appear when call 2 (/v1/images/generations) runs afterwards.
                                var (uploadedMediaId, uploadResponseJson) = await UploadReferenceImageAndGetMediaIdAsync(
                                    baseUrl, effApiKey, model, size, quality, item.Prompt, item.FlowProjectId, referenceImages, refWithFilePath);

                                if (!string.IsNullOrEmpty(uploadedMediaId))
                                {
                                    effectiveRefMediaId = uploadedMediaId;
                                    if (!string.IsNullOrEmpty(cacheKey))
                                    {
                                        lock (_uploadedReferenceMediaIds)
                                        {
                                            _uploadedReferenceMediaIds[cacheKey] = effectiveRefMediaId;
                                        }
                                    }
                                }

                                // If the upload response already carries an image payload, treat it
                                // as the final result for this item and skip the second call.
                                if (!string.IsNullOrEmpty(uploadResponseJson))
                                {
                                    try
                                    {
                                        using var probe = JsonDocument.Parse(uploadResponseJson);
                                        if (probe.RootElement.TryGetProperty("data", out var probeData)
                                            && probeData.ValueKind == JsonValueKind.Array
                                            && probeData.GetArrayLength() > 0)
                                        {
                                            var probeFirst = probeData[0];
                                            bool hasUrl = probeFirst.TryGetProperty("url", out var u) && !string.IsNullOrEmpty(u.GetString());
                                            bool hasB64 = probeFirst.TryGetProperty("b64_json", out var b) && !string.IsNullOrEmpty(b.GetString());
                                            if (hasUrl || hasB64)
                                            {
                                                await HandleOpenAiResponseAsync(item, uploadResponseJson, outputDirectory, uiContext);
                                                return;
                                            }
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        // Best-effort probe; fall through to call 2 below.
                                    }
                                }

                                // No usable image in upload response → continue to call 2.
                                if (string.IsNullOrEmpty(effectiveRefMediaId))
                                {
                                    SetItemStatusFailed(item, uiContext, "Flow API did not return media_id from reference upload.");
                                    return;
                                }
                            }
                        }
                        finally
                        {
                            _mediaIdLock.Release();
                        }
                    }
                }

                // Step 2: Perform Image Generation (POST /v1/images/generations with reference_media_id if available)
                await ProcessGenerationsAsync(item, baseUrl, effApiKey, model, size, quality, effectiveRefMediaId, outputDirectory, uiContext);
            }
            catch (Exception ex)
            {
                SetItemProperty(() =>
                {
                    item.Status = "Failed";
                    item.ErrorMessage = $"Error: {ex.Message}";
                    item.FinishedAt = DateTime.Now;
                });
            }
        }

        /// <summary>
        /// Creates a project directly on Google Flow (labs.google) via POST /v1/projects.
        /// </summary>
        public static async Task<(string? projectId, string? projectUrl, string? error)> CreateProjectAsync(string serverUrl, string apiKey, string projectTitle)
        {
            try
            {
                string baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:8787/v1" : serverUrl.TrimEnd('/');
                if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) && !baseUrl.Contains("/v1/"))
                {
                    baseUrl += "/v1";
                }
                string effApiKey = string.IsNullOrWhiteSpace(apiKey) ? "flow-local-key" : apiKey.Trim();
                string endpoint = $"{baseUrl}/projects";

                var payload = new Dictionary<string, string> { ["title"] = projectTitle };
                string jsonBody = JsonSerializer.Serialize(payload);

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effApiKey);

                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (null, null, FormatErrorMessage((int)response.StatusCode, responseContent));
                }

                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                string? pId = root.TryGetProperty("project_id", out var pidProp) ? pidProp.GetString() : null;
                string? pUrl = root.TryGetProperty("project_url", out var purlProp) ? purlProp.GetString() : null;

                return (pId, pUrl, null);
            }
            catch (Exception ex)
            {
                return (null, null, ex.Message);
            }
        }

        private async Task ProcessGenerationsAsync(
            BatchImageItem item,
            string baseUrl,
            string apiKey,
            string model,
            string size,
            string quality,
            string? referenceMediaId,
            string outputDirectory,
            SynchronizationContext? uiContext)
        {
            string endpoint = $"{baseUrl}/images/generations";

            var payload = new Dictionary<string, object>
            {
                ["model"] = model,
                ["prompt"] = item.Prompt,
                ["size"] = size,
                ["quality"] = quality,
                ["response_format"] = "url"
            };

            if (!string.IsNullOrWhiteSpace(referenceMediaId))
            {
                payload["reference_media_id"] = referenceMediaId;
            }

            if (!string.IsNullOrWhiteSpace(item.FlowProjectId))
            {
                payload["project_id"] = item.FlowProjectId;
            }
            else if (!string.IsNullOrWhiteSpace(item.FlowProjectTitle))
            {
                payload["project_title"] = item.FlowProjectTitle;
            }

            string jsonBody = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // Auto-fallback: If the project was deleted on Google Flow website (404/500/invalid project)
                if (!string.IsNullOrWhiteSpace(item.FlowProjectId) &&
                    (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                     responseContent.Contains("project", StringComparison.OrdinalIgnoreCase) ||
                     responseContent.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
                     responseContent.Contains("404", StringComparison.OrdinalIgnoreCase) ||
                     responseContent.Contains("UNAUTHORIZED", StringComparison.OrdinalIgnoreCase)))
                {
                    item.FlowProjectId = null;
                    string projectTitle = !string.IsNullOrWhiteSpace(item.FlowProjectTitle) ? item.FlowProjectTitle : "Batch Project";

                    var (newPid, newPurl, pErr) = await CreateProjectAsync(baseUrl, apiKey, projectTitle);
                    if (!string.IsNullOrEmpty(newPid))
                    {
                        item.FlowProjectId = newPid;
                        item.FlowProjectUrl = newPurl;

                        payload["project_id"] = newPid;
                        if (payload.ContainsKey("project_title")) payload.Remove("project_title");

                        string retryJson = JsonSerializer.Serialize(payload);
                        using var retryReq = new HttpRequestMessage(HttpMethod.Post, endpoint)
                        {
                            Content = new StringContent(retryJson, Encoding.UTF8, "application/json")
                        };
                        retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                        var retryResponse = await _httpClient.SendAsync(retryReq);
                        string retryContent = await retryResponse.Content.ReadAsStringAsync();

                        if (retryResponse.IsSuccessStatusCode)
                        {
                            await HandleOpenAiResponseAsync(item, retryContent, outputDirectory, uiContext);
                            return;
                        }
                    }
                }

                SetItemStatusFailed(item, uiContext, FormatErrorMessage((int)response.StatusCode, responseContent));
                return;
            }

            await HandleOpenAiResponseAsync(item, responseContent, outputDirectory, uiContext);
        }

        private static void SetItemStatusFailed(BatchImageItem item, SynchronizationContext? uiContext, string errorMessage)
        {
            void Apply()
            {
                item.Status = "Failed";
                item.ErrorMessage = errorMessage;
                item.FinishedAt = DateTime.Now;
            }
            if (uiContext != null && SynchronizationContext.Current != uiContext)
            {
                uiContext.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        private static void SetItemStatusDone(BatchImageItem item, SynchronizationContext? uiContext, string savedPath, string? mediaId, string? flowProjectId, string? flowProjectUrl)
        {
            void Apply()
            {
                if (!string.IsNullOrEmpty(mediaId)) item.MediaId = mediaId;
                if (!string.IsNullOrEmpty(flowProjectId)) item.FlowProjectId = flowProjectId;
                if (!string.IsNullOrEmpty(flowProjectUrl)) item.FlowProjectUrl = flowProjectUrl;
                item.ImagePath = savedPath;
                item.Status = "Done";
                item.FinishedAt = DateTime.Now;
            }
            if (uiContext != null && SynchronizationContext.Current != uiContext)
            {
                uiContext.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        private async Task<(string? mediaId, string? responseJson)> UploadReferenceImageAndGetMediaIdAsync(
            string baseUrl,
            string apiKey,
            string model,
            string size,
            string quality,
            string prompt,
            string? flowProjectId,
            List<(string base64Data, string tag)>? referenceImages,
            List<(string base64Data, string tag, string filePath)>? refWithFilePath)
        {
            string endpoint = $"{baseUrl}/images/edits";

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(model), "model");
            content.Add(new StringContent(prompt), "prompt");
            content.Add(new StringContent(size), "size");
            content.Add(new StringContent(quality), "quality");
            content.Add(new StringContent("url"), "response_format");

            if (!string.IsNullOrWhiteSpace(flowProjectId))
            {
                content.Add(new StringContent(flowProjectId), "project_id");
            }

            byte[] imageBytes = Array.Empty<byte>();
            string fileName = "image.png";

            if (refWithFilePath != null && refWithFilePath.Count > 0 && File.Exists(refWithFilePath[0].filePath))
            {
                imageBytes = await File.ReadAllBytesAsync(refWithFilePath[0].filePath);
                fileName = Path.GetFileName(refWithFilePath[0].filePath);
            }
            else if (referenceImages != null && referenceImages.Count > 0)
            {
                string b64 = referenceImages[0].base64Data;
                int commaIdx = b64.IndexOf(',');
                if (commaIdx >= 0) b64 = b64.Substring(commaIdx + 1);
                imageBytes = Convert.FromBase64String(b64);
            }

            if (imageBytes.Length == 0) return (null, null);

            var byteArrayContent = new ByteArrayContent(imageBytes);
            byteArrayContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            content.Add(byteArrayContent, "image", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return (null, responseContent);

            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array && dataArr.GetArrayLength() > 0)
            {
                var firstItem = dataArr[0];
                if (firstItem.TryGetProperty("media_id", out var mediaIdProp))
                {
                    return (mediaIdProp.GetString(), responseContent);
                }
            }

            // Response is OK but missing media_id; surface the JSON so the caller can still
            // attempt to use the image payload as a fallback.
            return (null, responseContent);
        }

        private async Task HandleOpenAiResponseAsync(BatchImageItem item, string jsonResponse, string outputDirectory, SynchronizationContext? uiContext)
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            var root = doc.RootElement;

            if (root.TryGetProperty("data", out var dataArr) && dataArr.ValueKind == JsonValueKind.Array && dataArr.GetArrayLength() > 0)
            {
                var firstItem = dataArr[0];

                string? mediaId = firstItem.TryGetProperty("media_id", out var mediaIdProp) ? mediaIdProp.GetString() : null;
                string? projId = firstItem.TryGetProperty("project_id", out var projIdProp) ? projIdProp.GetString() : null;
                string? projUrl = firstItem.TryGetProperty("project_url", out var projUrlProp) ? projUrlProp.GetString() : null;

                if (firstItem.TryGetProperty("url", out var urlProp) && !string.IsNullOrEmpty(urlProp.GetString()))
                {
                    string fileUrl = urlProp.GetString()!;
                    string savedPath = await DownloadOrSaveImageAsync(fileUrl, outputDirectory, item.Index);
                    SetItemStatusDone(item, uiContext, savedPath, mediaId, projId, projUrl);
                    return;
                }
                else if (firstItem.TryGetProperty("b64_json", out var b64Prop) && !string.IsNullOrEmpty(b64Prop.GetString()))
                {
                    byte[] bytes = Convert.FromBase64String(b64Prop.GetString()!);
                    string savedPath = Path.Combine(outputDirectory, $"flow_image_{item.Index}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    Directory.CreateDirectory(outputDirectory);
                    await File.WriteAllBytesAsync(savedPath, bytes);
                    SetItemStatusDone(item, uiContext, savedPath, mediaId, projId, projUrl);
                    return;
                }
            }

            SetItemStatusFailed(item, uiContext, "Flow API did not return image URL or base64 data.");
        }

        private async Task<string> DownloadOrSaveImageAsync(string fileUrl, string outputDir, int index)
        {
            Directory.CreateDirectory(outputDir);
            string fileName = $"flow_image_{index}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string localPath = Path.Combine(outputDir, fileName);

            if (fileUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                byte[] data = await _httpClient.GetByteArrayAsync(fileUrl);
                await File.WriteAllBytesAsync(localPath, data);
            }
            else if (File.Exists(fileUrl))
            {
                File.Copy(fileUrl, localPath, overwrite: true);
            }

            return localPath;
        }

        private static string FormatModelName(string rawModel, string aspectRatio)
        {
            if (string.IsNullOrWhiteSpace(rawModel)) rawModel = "gemini-3.1-flash-image";
            rawModel = rawModel.Trim();

            // Normalize: convert underscores to hyphens for Flow Local API compatibility
            // (e.g., "nano_banana_2" → "nano-banana-2")
            rawModel = rawModel.Replace('_', '-');

            if (rawModel.EndsWith("-landscape", StringComparison.OrdinalIgnoreCase) ||
                rawModel.EndsWith("-portrait", StringComparison.OrdinalIgnoreCase) ||
                rawModel.EndsWith("-square", StringComparison.OrdinalIgnoreCase) ||
                rawModel.EndsWith("-ultrawide", StringComparison.OrdinalIgnoreCase))
            {
                return rawModel;
            }

            string suffix = aspectRatio switch
            {
                "9:16" => "-portrait",
                "3:4" => "-portrait",
                "1:1" => "-square",
                "21:9" => "-ultrawide",
                _ => "-landscape"
            };

            return rawModel + suffix;
        }

        private static string FormatErrorMessage(int statusCode, string responseContent)
        {
            if (statusCode == 403 || responseContent.Contains("reCAPTCHA", StringComparison.OrdinalIgnoreCase))
            {
                return "HTTP 403: reCAPTCHA evaluation failed. Please open http://127.0.0.1:8787/setup in browser to sign in.";
            }

            return $"HTTP {statusCode}: {responseContent}";
        }

        private static string MapSize(string aspect)
        {
            return aspect switch
            {
                "9:16" => "1024x1536",
                "1:1" => "1024x1024",
                "4:3" => "1536x1024",
                "3:4" => "1024x1536",
                _ => "1536x1024" // 16:9 landscape default
            };
        }

        private static string MapQuality(string upscale)
        {
            return upscale?.ToUpper() switch
            {
                "2K" => "hd",
                "4K" => "4k",
                _ => "standard"
            };
        }
    }
}