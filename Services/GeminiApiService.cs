using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Helpers;

namespace AssetAutomator.Services
{
    public class GemModel
    {
        public string id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string prompt { get; set; } = string.Empty;
        public bool predefined { get; set; }
    }

    public class GeminiChatResponseModel
    {
        public string session_id { get; set; } = string.Empty;
        public string text { get; set; } = string.Empty;
        public string thoughts { get; set; } = string.Empty;
        public List<string> images { get; set; } = new List<string>();
        public bool deep_research_completed { get; set; }
    }

    public class DeepResearchStartResponseModel
    {
        public string research_id { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string plan_title { get; set; } = string.Empty;
        public List<string> steps { get; set; } = new List<string>();
    }

    public class DeepResearchStatusResponseModel
    {
        public string research_id { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty; // "RUNNING", "COMPLETED", "FAILED"
        public double elapsed_seconds { get; set; }
        public string plan_title { get; set; } = string.Empty;
        public List<string> steps { get; set; } = new List<string>();
        public string text { get; set; } = string.Empty;
        public string error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service for interacting with the Gemini WebAPI REST Server (Modules/Gemini-API-2.0.0).
    /// Handles listing gems, sending chat prompts (with Deep Research & Gem selection), and health checks.
    /// </summary>
    public class GeminiApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        /// <summary>
        /// Converts a GemOptionItem (UI model) to a GemModel (API model).
        /// Used by PipelineOrchestrator to bridge UI selection with API calls.
        /// </summary>
        public static GemModel? ConvertFromGemOption(Models.Nodes.GemOptionItem? option)
        {
            if (option == null) return null;
            return new GemModel
            {
                id = option.Id,
                name = option.Name
            };
        }

        public GeminiApiService(string? baseUrl = null)
        {
            _baseUrl = (baseUrl ?? ConfigService.CurrentSettings.GeminiApiBaseUrl ?? "http://localhost:8000").TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        /// <summary>
        /// Verifies server health and launches Python background server if not running.
        /// </summary>
        public async Task<bool> EnsureConnectedAsync()
        {
            return await PythonServerManager.EnsureServerRunningAsync(_baseUrl);
        }

        /// <summary>
        /// Fetches available Gemini Gems (Custom Gems + System Gems).
        /// </summary>
        public async Task<List<GemModel>> GetGemsAsync(bool includeHidden = true)
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/gems{(includeHidden ? "?include_hidden=true" : "")}";
            var response = await _httpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gemini REST Server error ({response.StatusCode}): {content}");
            }

            var gems = JsonSerializer.Deserialize<List<GemModel>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return gems ?? new List<GemModel>();
        }

        /// <summary>
        /// Sends a chat prompt to Gemini WebAPI (with support for Gem selection, Deep Research, and Session tracking).
        /// </summary>
        public async Task<GeminiChatResponseModel> SendChatAsync(
            string message,
            string? gemId = null,
            string? model = null,
            string? extension = null,
            bool deepResearch = false,
            string? sessionId = null,
            bool temporary = false,
            List<string>? filePaths = null)
        {
            await EnsureConnectedAsync();

            string finalMessage = message;
            if (!string.IsNullOrWhiteSpace(extension) && !extension.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                if (!finalMessage.StartsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    finalMessage = $"{extension} {finalMessage}";
                }
            }

            var payload = new Dictionary<string, object>
            {
                { "message", finalMessage },
                { "deep_research", deepResearch },
                { "temporary", temporary }
            };

            if (!string.IsNullOrWhiteSpace(gemId))
            {
                payload["gem_id"] = gemId;
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                payload["model"] = model;
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                payload["session_id"] = sessionId;
            }

            if (filePaths != null && filePaths.Count > 0)
            {
                payload["files"] = filePaths;
            }

            string jsonContent = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            string url = $"{_baseUrl}/api/chat";
            var response = await _httpClient.PostAsync(url, content);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gemini API error ({response.StatusCode}): {responseText}");
            }

            var result = JsonSerializer.Deserialize<GeminiChatResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
            {
                throw new InvalidOperationException("Failed to deserialize Gemini API response.");
            }

            return result;
        }

        /// <summary>
        /// Starts an asynchronous Deep Research job on the Python REST server and returns job metadata.
        /// </summary>
        public async Task<DeepResearchStartResponseModel> StartDeepResearchAsync(
            string message,
            string? gemId = null,
            string? model = null,
            string? sessionId = null)
        {
            await EnsureConnectedAsync();

            var payload = new Dictionary<string, object>
            {
                { "message", message }
            };
            if (!string.IsNullOrWhiteSpace(gemId)) payload["gem_id"] = gemId;
            if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model;
            if (!string.IsNullOrWhiteSpace(sessionId)) payload["session_id"] = sessionId;

            string jsonContent = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            string url = $"{_baseUrl}/api/deep-research/start";
            var response = await _httpClient.PostAsync(url, content);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gemini Deep Research start error ({response.StatusCode}): {responseText}");
            }

            var result = JsonSerializer.Deserialize<DeepResearchStartResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? throw new InvalidOperationException("Failed to deserialize Deep Research start response.");
        }

        /// <summary>
        /// Polls the status of an active Deep Research job.
        /// </summary>
        public async Task<DeepResearchStatusResponseModel> GetDeepResearchStatusAsync(string researchId)
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/deep-research/status/{researchId}";
            var response = await _httpClient.GetAsync(url);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Gemini Deep Research status error ({response.StatusCode}): {responseText}");
            }

            var result = JsonSerializer.Deserialize<DeepResearchStatusResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? throw new InvalidOperationException("Failed to deserialize Deep Research status response.");
        }

        /// <summary>
        /// High-level helper: Starts Deep Research and polls status continuously (every pollIntervalMs)
        /// with live callbacks until finished or timeout.
        /// </summary>
        public async Task<DeepResearchStatusResponseModel> ExecuteDeepResearchWithProgressAsync(
            string message,
            string? gemId,
            string? model,
            string? sessionId,
            Action<string> onProgress,
            int pollIntervalMs = 5000,
            int timeoutSeconds = 600)
        {
            onProgress("[DEEP-RESEARCH] 🚀 Creating Deep Research plan & starting background agent...");
            var startResp = await StartDeepResearchAsync(message, gemId, model, sessionId);

            onProgress($"[DEEP-RESEARCH] 📋 Research Plan: '{startResp.plan_title}'");
            if (startResp.steps != null && startResp.steps.Count > 0)
            {
                foreach (var step in startResp.steps)
                {
                    onProgress($"[DEEP-RESEARCH]   • Step: {step}");
                }
            }

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
            {
                await Task.Delay(pollIntervalMs);
                var statusResp = await GetDeepResearchStatusAsync(startResp.research_id);

                if (statusResp.status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress($"[DEEP-RESEARCH] ✅ Research completed in {statusResp.elapsed_seconds:F1}s! Report length: {statusResp.text?.Length ?? 0} chars.");
                    return statusResp;
                }
                else if (statusResp.status.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"[DEEP-RESEARCH] Failed: {statusResp.error}");
                }
                else
                {
                    onProgress($"[DEEP-RESEARCH-POLL] ⏳ Deep Research in progress... Elapsed: {statusResp.elapsed_seconds:F0}s (Checking internet sources & aggregating data...)");
                }
            }

            throw new TimeoutException($"[DEEP-RESEARCH] Research timed out after {timeoutSeconds}s.");
        }

        /// <summary>
        /// Triggers Python REST server to reload cookies.json and re-initialize GeminiClient.
        /// </summary>
        public async Task<bool> RefreshCookiesAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                string url = $"{_baseUrl}/api/refresh-cookies";
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Triggers Python REST server to read installed browser cookies directly and re-initialize GeminiClient.
        /// </summary>
        public async Task<bool> RefreshFromBrowserAsync()
        {
            if (!await EnsureConnectedAsync()) return false;
            try
            {
                string url = $"{_baseUrl}/api/refresh-from-browser";
                using var content = new StringContent("{}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}
