using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Step 5: Generates edited images via the OpenAI-compatible
    /// <c>/v1/images/edits</c> endpoint exposed by the Google Flow Local server
    /// (<c>D:\Dev\google-flow-2.0.0</c>, default <c>http://127.0.0.1:8787/v1</c>).
    /// </summary>
    public class ImageGenerationStep
    {
        private readonly ImagePoolService _poolService;
        private readonly IConfigService _configService;

        public ImageGenerationStep(ImagePoolService poolService, IConfigService configService)
        {
            _poolService = poolService;
            _configService = configService;
            _poolService.EditImageFunc = EditImageViaApiAsync;
        }

        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 5] Starting Image Generation via Google Flow Local API...");

            string thumbnailPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");
            if (!File.Exists(thumbnailPath))
            {
                throw new FileNotFoundException($"[STEP 5] [ERROR] Thumbnail file not found: {thumbnailPath}. Please run Step 1 first.");
            }

            string apiUrl = ResolveApiUrl();
            string apiKey = _configService.CurrentSettings.ImageApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "flow-local-key";
            }

            // Image 1: Translation
            string prompt1 = $"tạo một bức ảnh tương tự với phần văn bản được dịch sang ngôn ngữ '{task.TargetLanguage}', kích thước ảnh 16:9";
            string savePath1 = Path.Combine(task.OutputDir, "translated_thumbnail.png");

            // Image 2: Clean/Remove elements
            string prompt2 = "tạo một bức ảnh tương tự với phần văn bản, biểu tượng mũi tên, vòng tròn (nếu có) được xóa, kích thước ảnh 16:9";
            string savePath2 = Path.Combine(task.OutputDir, "cleaned_thumbnail.png");

            var tasksToEnqueue = new System.Collections.Generic.List<Task>();
            var tasksToAwait = new System.Collections.Generic.List<Task>();

            if (File.Exists(savePath1))
            {
                logTask(task, "[STEP 5] translated_thumbnail.png already exists. Skipping Image 1.");
            }
            else
            {
                var req1 = new ImageGenRequest
                {
                    ApiUrl = apiUrl,
                    ApiKey = apiKey,
                    ImagePath = thumbnailPath,
                    Prompt = prompt1,
                    SavePath = savePath1,
                    Task = task
                };
                tasksToEnqueue.Add(_poolService.EnqueueImageRequestAsync(req1));
                tasksToAwait.Add(req1.Tcs.Task);
            }

            if (File.Exists(savePath2))
            {
                logTask(task, "[STEP 5] cleaned_thumbnail.png already exists. Skipping Image 2.");
            }
            else
            {
                var req2 = new ImageGenRequest
                {
                    ApiUrl = apiUrl,
                    ApiKey = apiKey,
                    ImagePath = thumbnailPath,
                    Prompt = prompt2,
                    SavePath = savePath2,
                    Task = task
                };
                tasksToEnqueue.Add(_poolService.EnqueueImageRequestAsync(req2));
                tasksToAwait.Add(req2.Tcs.Task);
            }

            if (tasksToEnqueue.Count > 0)
            {
                logTask(task, $"[STEP 5] Enqueuing {tasksToEnqueue.Count} Image Generation request(s) to the pool...");
                await Task.WhenAll(tasksToEnqueue);
                await Task.WhenAll(tasksToAwait);
                logTask(task, "[STEP 5] Image generation request(s) completed successfully!");
            }
            else
            {
                logTask(task, "[STEP 5] All thumbnails already exist. Skipping Step 5 entirely.");
            }
        }

        private string ResolveApiUrl()
        {
            string apiUrl = _configService.CurrentSettings.ImageApiUrl;
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                return "http://127.0.0.1:8787/v1/images/edits";
            }

            apiUrl = apiUrl.TrimEnd('/');

            // If user already pointed at the edits endpoint, keep as-is.
            if (apiUrl.EndsWith("/images/edits", StringComparison.OrdinalIgnoreCase) ||
                apiUrl.EndsWith("/images/edits/", StringComparison.OrdinalIgnoreCase))
            {
                return apiUrl;
            }

            // Otherwise normalize: ensure /v1 suffix, then append /images/edits.
            if (!apiUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                apiUrl += "/v1";
            }

            return apiUrl + "/images/edits";
        }

        /// <summary>
        /// Performs the actual image edit via the Google Flow Local
        /// OpenAI-compatible <c>/v1/images/edits</c> endpoint.
        /// </summary>
        private async Task EditImageViaApiAsync(ImageGenRequest req)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            if (!string.IsNullOrWhiteSpace(req.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", req.ApiKey);
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("nano-banana-2"), "model");
            content.Add(new StringContent(req.Prompt), "prompt");
            content.Add(new StringContent("1"), "n");

            byte[] fileBytes = await File.ReadAllBytesAsync(req.ImagePath);
            var imageContent = new ByteArrayContent(fileBytes);
            string contentType = req.ImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            imageContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            content.Add(imageContent, "image", Path.GetFileName(req.ImagePath));

            var response = await httpClient.PostAsync(req.ApiUrl, content);
            string responseContent = await response.Content.ReadAsStringAsync();

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("_account_email", out var emailProp))
                {
                    req.AccountName = emailProp.GetString() ?? string.Empty;
                }
                else if (doc.RootElement.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.Object)
                {
                    if (errorProp.TryGetProperty("account_email", out var errEmailProp))
                    {
                        req.AccountName = errEmailProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                // ignore parse errors - we'll throw a clearer one below if needed
            }

            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Flow Local API status {response.StatusCode}. Details: {responseContent}");
                }

                if (doc != null && doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array && dataArray.GetArrayLength() > 0)
                {
                    var firstItem = dataArray[0];
                    string? imgUrl = null;
                    if (firstItem.TryGetProperty("url", out var urlProp))
                    {
                        imgUrl = urlProp.GetString();
                    }
                    else if (firstItem.TryGetProperty("b64_json", out var b64Prop))
                    {
                        string b64 = b64Prop.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(b64))
                        {
                            byte[] imgBytes = Convert.FromBase64String(b64);
                            await File.WriteAllBytesAsync(req.SavePath, imgBytes);
                            return;
                        }
                    }

                    if (!string.IsNullOrEmpty(imgUrl))
                    {
                        if (imgUrl.StartsWith("/"))
                        {
                            var uri = new Uri(req.ApiUrl);
                            imgUrl = $"{uri.Scheme}://{uri.Authority}{imgUrl}";
                        }
                        var imgData = await httpClient.GetByteArrayAsync(imgUrl);
                        await File.WriteAllBytesAsync(req.SavePath, imgData);
                    }
                    else
                    {
                        throw new Exception("API response contains no image URL or Base64 data.");
                    }
                }
                else
                {
                    throw new Exception("No image data returned in API response.");
                }
            }
            finally
            {
                doc?.Dispose();
            }
        }
    }
}