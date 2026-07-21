using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AssetAutomator
{
    /// <summary>
    /// Step 5: Generates edited images via Image Edits API (Legacy or G-Labs).
    /// Enqueues requests to the ImagePoolService for concurrent processing.
    /// Also contains the actual API call logic (EditImageViaApiAsync).
    /// </summary>
    public class ImageGenerationStep
    {
        private readonly ImagePoolService _poolService;

        public ImageGenerationStep(ImagePoolService poolService)
        {
            _poolService = poolService;
            // Wire up the pool's edit function to our API method
            _poolService.EditImageFunc = EditImageViaApiAsync;
        }

        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 5] Starting Image Generation via Image Edits API Request Pool...");

            string thumbnailPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");
            if (!File.Exists(thumbnailPath))
            {
                throw new FileNotFoundException($"[STEP 5] [ERROR] Thumbnail file not found: {thumbnailPath}. Please run Step 1 first.");
            }

            string apiUrl = ResolveApiUrl();
            string apiKey = ConfigService.CurrentSettings.ImageApiKey;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "chatgpt2api";
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
            string apiUrl = ConfigService.CurrentSettings.ImageApiUrl;

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                return "http://localhost:8000/v1/images/edits";
            }

            // Check if the URL is for G-Labs Webhook API
            bool isGlabs = apiUrl.Contains("8765") ||
                           apiUrl.Contains("ngrok-free.dev") ||
                           apiUrl.Contains("/api/") ||
                           apiUrl.Contains("/api/image");

            if (!isGlabs && !apiUrl.EndsWith("/images/edits"))
            {
                apiUrl = apiUrl.TrimEnd('/');
                if (apiUrl.EndsWith("/v1"))
                {
                    apiUrl += "/images/edits";
                }
                else
                {
                    apiUrl += "/v1/images/edits";
                }
            }

            return apiUrl;
        }

        /// <summary>
        /// Performs the actual image generation API call. Handles both Legacy (OpenAI-compatible) and G-Labs APIs.
        /// </summary>
        private async Task EditImageViaApiAsync(ImageGenRequest req)
        {
            var task = req.Task;
            var apiUrl = req.ApiUrl;
            var apiKey = req.ApiKey;
            var imagePath = req.ImagePath;
            var prompt = req.Prompt;
            var savePath = req.SavePath;

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            bool isLegacyApi = apiUrl.EndsWith("/images/edits", StringComparison.OrdinalIgnoreCase) ||
                               apiUrl.EndsWith("/images/edits/", StringComparison.OrdinalIgnoreCase);

            if (isLegacyApi)
            {
                await ExecuteLegacyApiAsync(httpClient, req);
            }
            else
            {
                await ExecuteGlabsApiAsync(httpClient, req);
            }
        }

        private async Task ExecuteLegacyApiAsync(HttpClient httpClient, ImageGenRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", req.ApiKey);
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("gpt-image-2"), "model");
            content.Add(new StringContent(req.Prompt), "prompt");
            content.Add(new StringContent("1"), "n");

            byte[] fileBytes = await File.ReadAllBytesAsync(req.ImagePath);
            var imageContent = new ByteArrayContent(fileBytes);
            string contentType = req.ImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            imageContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            content.Add(imageContent, "image", Path.GetFileName(req.ImagePath));

            var response = await httpClient.PostAsync(req.ApiUrl, content);
            string responseContent = await response.Content.ReadAsStringAsync();

            System.Text.Json.JsonDocument? doc = null;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("_account_email", out var emailProp))
                {
                    req.AccountName = emailProp.GetString() ?? string.Empty;
                }
                else if (doc.RootElement.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (errorProp.TryGetProperty("account_email", out var errEmailProp))
                    {
                        req.AccountName = errEmailProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch { }

            try
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"API status code {response.StatusCode}. Details: {responseContent}");
                }

                if (doc != null && doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == System.Text.Json.JsonValueKind.Array && dataArray.GetArrayLength() > 0)
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

        private async Task ExecuteGlabsApiAsync(HttpClient httpClient, ImageGenRequest req)
        {
            var logTask = _poolService.LogTask;

            string normalizedApiUrl = req.ApiUrl.TrimEnd('/');
            if (!normalizedApiUrl.EndsWith("/api/image/generate", StringComparison.OrdinalIgnoreCase))
            {
                normalizedApiUrl += "/api/image/generate";
            }

            // 1. Prepare base64 image
            byte[] fileBytes = await File.ReadAllBytesAsync(req.ImagePath);
            string base64Data = Convert.ToBase64String(fileBytes);
            string extension = Path.GetExtension(req.ImagePath).ToLower();
            string mimeType = extension == ".png" ? "image/png" : "image/jpeg";
            string base64Uri = $"data:{mimeType};base64,{base64Data}";

            // 2. Prepare payload
            var payload = new
            {
                prompt = req.Prompt,
                model = "nano_banana_2",
                aspect_ratio = "16:9",
                reference_images = new[] { base64Uri }
            };
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);

            // 3. Send request
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, normalizedApiUrl);
            if (!string.IsNullOrWhiteSpace(req.ApiKey))
            {
                requestMessage.Headers.Add("X-API-Key", req.ApiKey);
            }
            requestMessage.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");

            // Output equivalent cURL command for debugging
            string truncatedPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                prompt = req.Prompt,
                model = "nano_banana_2",
                aspect_ratio = "16:9",
                reference_images = new[] { $"data:{mimeType};base64,[BASE64_IMAGE_DATA_TRUNCATED]" }
            });
            string curlCmd = $"curl -X POST \"{normalizedApiUrl}\" " +
                             $"-H \"X-API-Key: {req.ApiKey}\" " +
                             $"-H \"Content-Type: application/json\" " +
                             $"-d '{truncatedPayload}'";
            logTask?.Invoke(req.Task, $"[STEP 5] Equivalent cURL command:\n{curlCmd}");

            logTask?.Invoke(req.Task, $"[STEP 5] Sending image generation request to G-Labs API: {normalizedApiUrl}...");
            var response = await httpClient.SendAsync(requestMessage);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"G-Labs API error (status code {response.StatusCode}): {responseContent}");
            }

            // 4. Parse task_id
            using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
            if (!doc.RootElement.TryGetProperty("task_id", out var taskIdProp))
            {
                throw new Exception($"G-Labs API response does not contain 'task_id'. Response: {responseContent}");
            }
            string taskId = taskIdProp.GetString() ?? throw new Exception("task_id is null");
            logTask?.Invoke(req.Task, $"[STEP 5] G-Labs API Task created successfully. Task ID: {taskId}");

            // 5. Polling status
            string apiBaseUrl = new Uri(normalizedApiUrl).GetLeftPart(UriPartial.Authority);
            string statusUrl = $"{apiBaseUrl}/api/status/{taskId}";

            string statusCurl = $"curl -X GET \"{statusUrl}\" -H \"X-API-Key: {req.ApiKey}\"";
            logTask?.Invoke(req.Task, $"[STEP 5] Status check cURL command:\n{statusCurl}");

            bool isCompleted = false;
            int attempts = 0;
            string? downloadUrl = null;

            while (!isCompleted)
            {
                attempts++;
                await Task.Delay(3000);

                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                if (!string.IsNullOrWhiteSpace(req.ApiKey))
                {
                    statusRequest.Headers.Add("X-API-Key", req.ApiKey);
                }

                var statusResponse = await httpClient.SendAsync(statusRequest);
                if (!statusResponse.IsSuccessStatusCode)
                {
                    logTask?.Invoke(req.Task, $"[STEP 5] [WARNING] Polling status failed (Attempt {attempts}). Status: {statusResponse.StatusCode}");
                    continue;
                }

                string statusJson = await statusResponse.Content.ReadAsStringAsync();
                using var statusDoc = System.Text.Json.JsonDocument.Parse(statusJson);
                var root = statusDoc.RootElement;

                if (root.TryGetProperty("status", out var statusPropVal))
                {
                    string status = statusPropVal.GetString() ?? "pending";
                    if (status == "completed")
                    {
                        isCompleted = true;
                        if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == System.Text.Json.JsonValueKind.Array && resultsProp.GetArrayLength() > 0)
                        {
                            downloadUrl = resultsProp[0].GetString();
                        }
                        else
                        {
                            throw new Exception("G-Labs task completed but results are empty.");
                        }
                    }
                    else if (status == "failed")
                    {
                        string errMsg = "Unknown error";
                        if (root.TryGetProperty("error", out var errProp)) errMsg = errProp.GetString() ?? errMsg;
                        throw new Exception($"G-Labs image generation failed: {errMsg}");
                    }
                    else
                    {
                        if (attempts % 5 == 0)
                        {
                            logTask?.Invoke(req.Task, $"[STEP 5] Polling status for task {taskId}: {status}...");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                throw new Exception("Image download URL not found in results.");
            }

            // 6. Rewrite download URL if loopback
            if (!downloadUrl.StartsWith(apiBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                var resultUri = new Uri(downloadUrl);
                downloadUrl = apiBaseUrl + resultUri.PathAndQuery;
            }

            logTask?.Invoke(req.Task, $"[STEP 5] Downloading generated image from: {downloadUrl}...");
            var imgData = await httpClient.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(req.SavePath, imgData);
            logTask?.Invoke(req.Task, $"[STEP 5] Successfully saved generated image to: {req.SavePath}");
        }
    }
}
