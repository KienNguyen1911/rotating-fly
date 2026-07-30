using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services.Providers
{
    /// <summary>
    /// Concrete Strategy for G-Labs Webhook API (:8765).
    /// </summary>
    public class GlabsImageGenProvider : IImageGenProvider
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        public string ProviderKey => "glabs";

        public async Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory)
        {
            item.StartedAt = DateTime.Now;
            item.Status = "Processing";

            string baseUrl = string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:8765" : serverUrl.TrimEnd('/');
            string endpoint = item.Engine switch
            {
                "meta" => "/api/meta/generate",
                "grok" => "/api/grok/generate",
                _ => "/api/image/generate"
            };

            var requestBody = new Dictionary<string, object>();
            requestBody["prompt"] = item.Prompt;
            requestBody["aspect_ratio"] = item.AspectRatio;

            if (item.Engine == "flow")
            {
                requestBody["model"] = item.Model;
                if (!string.IsNullOrEmpty(item.Upscale) && item.Upscale.ToLower() != "none")
                {
                    requestBody["upscale"] = new string[] { item.Upscale };
                }

                if (referenceImages.Count > 0)
                {
                    var refList = new List<object>();
                    foreach (var refImg in referenceImages)
                    {
                        string tagName = refImg.tag.TrimStart('@');
                        if (!tagName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            tagName += ".png";
                        }

                        refList.Add(new
                        {
                            data = refImg.base64Data,
                            name = tagName
                        });
                    }
                    requestBody["reference_images"] = refList;
                }
            }
            else if (item.Engine == "meta")
            {
                requestBody["mode"] = referenceImages.Count > 0 ? "i2i" : "t2i";
                if (referenceImages.Count > 0)
                {
                    requestBody["character_image"] = referenceImages[0].base64Data;
                }
            }
            else if (item.Engine == "grok")
            {
                requestBody["mode"] = referenceImages.Count > 0 ? "i2i" : "t2i";
                if (referenceImages.Count > 0)
                {
                    var refList = new List<string>();
                    foreach (var r in referenceImages)
                    {
                        refList.Add(r.base64Data);
                    }
                    requestBody["reference_images"] = refList;
                }
            }

            string jsonString = JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

            using var reqMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{endpoint}")
            {
                Content = content
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                reqMessage.Headers.Add("X-API-Key", apiKey);
            }

            var response = await _httpClient.SendAsync(reqMessage);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                item.Status = "Failed";
                item.ErrorMessage = $"HTTP {(int)response.StatusCode}: {responseContent}";
                item.FinishedAt = DateTime.Now;
                return;
            }

            using var doc = JsonDocument.Parse(responseContent);
            if (!doc.RootElement.TryGetProperty("task_id", out var taskIdProp))
            {
                item.Status = "Failed";
                item.ErrorMessage = "G-Labs API response did not contain task_id.";
                item.FinishedAt = DateTime.Now;
                return;
            }

            item.TaskId = taskIdProp.GetString() ?? string.Empty;

            // Poll task status until complete or failed
            await PollTaskStatusAsync(item, baseUrl, apiKey, outputDirectory);
        }

        private async Task PollTaskStatusAsync(BatchImageItem item, string baseUrl, string apiKey, string outputDir, int maxAttempts = 180)
        {
            string statusUrl = $"{baseUrl}/api/status/{item.TaskId}";

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                await Task.Delay(3000); // Poll every 3 seconds

                using var req = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    req.Headers.Add("X-API-Key", apiKey);
                }

                try
                {
                    var response = await _httpClient.SendAsync(req);
                    if (!response.IsSuccessStatusCode) continue;

                    string content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("status", out var statusProp))
                    {
                        string status = statusProp.GetString() ?? string.Empty;
                        if (status == "completed")
                        {
                            if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array && resultsProp.GetArrayLength() > 0)
                            {
                                string fileUrl = resultsProp[0].GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(fileUrl))
                                {
                                    string savedPath = await DownloadImageFileAsync(fileUrl, outputDir, item.Index);
                                    item.ImagePath = savedPath;
                                    item.Status = "Done";
                                    item.FinishedAt = DateTime.Now;
                                    return;
                                }
                            }
                            item.Status = "Done";
                            item.FinishedAt = DateTime.Now;
                            return;
                        }
                        else if (status == "failed")
                        {
                            item.Status = "Failed";
                            string err = root.TryGetProperty("error", out var errProp) ? errProp.GetString() ?? "Task failed" : "Task failed";
                            item.ErrorMessage = err;
                            item.FinishedAt = DateTime.Now;
                            return;
                        }
                    }
                }
                catch
                {
                    // Ignore transient network errors
                }
            }

            item.Status = "Failed";
            item.ErrorMessage = "Timeout: Task did not complete within 9 minutes.";
            item.FinishedAt = DateTime.Now;
        }

        private async Task<string> DownloadImageFileAsync(string fileUrl, string outputDir, int index)
        {
            Directory.CreateDirectory(outputDir);
            string ext = ".png";
            if (fileUrl.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || fileUrl.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".jpg";
            }

            string fileName = $"glabs_image_{index}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            string localPath = Path.Combine(outputDir, fileName);

            byte[] data = await _httpClient.GetByteArrayAsync(fileUrl);
            await File.WriteAllBytesAsync(localPath, data);

            return localPath;
        }
    }
}