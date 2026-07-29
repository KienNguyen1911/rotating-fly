using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AssetAutomator.Application.Services
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
        public List<string> images { get; set; } = new();
        public bool deep_research_completed { get; set; }
    }

    public class DeepResearchStartResponseModel
    {
        public string research_id { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string plan_title { get; set; } = string.Empty;
        public List<string> steps { get; set; } = new();
    }

    public class DeepResearchStatusResponseModel
    {
        public string research_id { get; set; } = string.Empty;
        public string session_id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public double elapsed_seconds { get; set; }
        public string plan_title { get; set; } = string.Empty;
        public List<string> steps { get; set; } = new();
        public string text { get; set; } = string.Empty;
        public string error { get; set; } = string.Empty;
    }

    public class AvailableModelInfo
    {
        public string model_id { get; set; } = string.Empty;
        public string model_name { get; set; } = string.Empty;
        public string display_name { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public int capacity { get; set; }
        public bool is_available { get; set; }
        public bool is_thinking { get; set; }
        public bool is_advanced_only { get; set; }
    }

    /// <summary>
    /// Service for interacting with the Gemini WebAPI REST Server.
    /// </summary>
    public class GeminiApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Infrastructure.Helpers.PythonServerManager _pythonServerManager;

        public static string ResolveModelName(string? modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName) && modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                return modelName.Trim();
            return "gemini-3-flash";
        }

        public GeminiApiService(string? baseUrl = null, Infrastructure.Helpers.PythonServerManager? pythonServerManager = null)
        {
            _baseUrl = (baseUrl ?? Infrastructure.Services.ConfigService.CurrentSettings.GeminiApiBaseUrl ?? "http://localhost:8000").TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _pythonServerManager = pythonServerManager ?? Infrastructure.Helpers.PythonServerManager.Default;
        }

        public async Task<bool> EnsureConnectedAsync()
        {
            var (success, _) = await _pythonServerManager.EnsureServerRunningAsync(_baseUrl);
            return success;
        }

        public async Task<List<GemModel>> GetGemsAsync(bool includeHidden = true)
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/gems{(includeHidden ? "?include_hidden=true" : "")}";
            var response = await _httpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini REST Server error ({response.StatusCode}): {content}");

            var gems = JsonSerializer.Deserialize<List<GemModel>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return gems ?? new List<GemModel>();
        }

        public async Task<GeminiChatResponseModel> SendChatAsync(
            string message,
            string? gemId = null,
            string? model = null,
            bool deepResearch = false,
            string? sessionId = null,
            bool temporary = false,
            List<string>? filePaths = null)
        {
            await EnsureConnectedAsync();
            string resolvedModel = ResolveModelName(model);

            bool isThinking = resolvedModel.Contains("pro", StringComparison.OrdinalIgnoreCase);
            string thinkingTag = isThinking ? "THINKING (Pro)" : "STANDARD";
            System.Diagnostics.Debug.WriteLine($"[GEMINI-API] SendChat: model={resolvedModel} | {thinkingTag} | gem={gemId ?? "default"} | deepResearch={deepResearch}");
            Console.WriteLine($"[GEMINI-API] SendChat: model={resolvedModel} | {thinkingTag} | gem={gemId ?? "default"}");

            var payload = new Dictionary<string, object>
            {
                { "message", message },
                { "deep_research", deepResearch },
                { "temporary", temporary },
                { "model", resolvedModel }
            };

            if (!string.IsNullOrWhiteSpace(gemId))
                payload["gem_id"] = gemId;
            if (!string.IsNullOrWhiteSpace(sessionId))
                payload["session_id"] = sessionId;
            if (filePaths != null && filePaths.Count > 0)
                payload["files"] = filePaths;

            string jsonContent = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            string url = $"{_baseUrl}/api/chat";
            var response = await _httpClient.PostAsync(url, content);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini API error ({response.StatusCode}): {responseText}");

            var result = JsonSerializer.Deserialize<GeminiChatResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null)
                throw new InvalidOperationException("Failed to deserialize Gemini API response.");

            return result;
        }

        public async Task<DeepResearchStartResponseModel> StartDeepResearchAsync(
            string message, string? gemId = null, string? model = null, string? sessionId = null)
        {
            await EnsureConnectedAsync();

            bool isThinking = model != null && model.Contains("thinking", StringComparison.OrdinalIgnoreCase);
            string tier = model != null && model.Contains("advanced", StringComparison.OrdinalIgnoreCase) ? "Advanced" :
                          model != null && model.Contains("plus", StringComparison.OrdinalIgnoreCase) ? "Plus" : "Basic";
            string thinkingTag = isThinking ? $"THINKING ({tier})" : $"STANDARD ({tier})";
            System.Diagnostics.Debug.WriteLine($"[GEMINI-API] StartDeepResearch: model={model ?? "default"} | {thinkingTag} | gem={gemId ?? "default"}");
            Console.WriteLine($"[GEMINI-API] StartDeepResearch: model={model ?? "default"} | {thinkingTag} | gem={gemId ?? "default"}");

            var payload = new Dictionary<string, object> { { "message", message } };
            if (!string.IsNullOrWhiteSpace(gemId)) payload["gem_id"] = gemId;
            if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model;
            if (!string.IsNullOrWhiteSpace(sessionId)) payload["session_id"] = sessionId;

            string jsonContent = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            string url = $"{_baseUrl}/api/deep-research/start";
            var response = await _httpClient.PostAsync(url, content);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini Deep Research start error ({response.StatusCode}): {responseText}");

            var result = JsonSerializer.Deserialize<DeepResearchStartResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? throw new InvalidOperationException("Failed to deserialize Deep Research start response.");
        }

        public async Task<DeepResearchStatusResponseModel> GetDeepResearchStatusAsync(string researchId)
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/deep-research/status/{researchId}";
            var response = await _httpClient.GetAsync(url);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Gemini Deep Research status error ({response.StatusCode}): {responseText}");

            var result = JsonSerializer.Deserialize<DeepResearchStatusResponseModel>(responseText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? throw new InvalidOperationException("Failed to deserialize Deep Research status response.");
        }

        public async Task<DeepResearchStatusResponseModel> ExecuteDeepResearchWithProgressAsync(
            string message, string? gemId, string? model, string? sessionId, Action<string> onProgress,
            int pollIntervalMs = 5000, int timeoutSeconds = 600)
        {
            onProgress("[DEEP-RESEARCH] Creating Deep Research plan & starting background agent...");
            var startResp = await StartDeepResearchAsync(message, gemId, model, sessionId);

            onProgress($"[DEEP-RESEARCH] Research Plan: '{startResp.plan_title}'");
            if (startResp.steps != null && startResp.steps.Count > 0)
                foreach (var step in startResp.steps)
                    onProgress($"[DEEP-RESEARCH]   - Step: {step}");

            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
            {
                await Task.Delay(pollIntervalMs);
                var statusResp = await GetDeepResearchStatusAsync(startResp.research_id);

                if (statusResp.status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    onProgress($"[DEEP-RESEARCH] Research completed in {statusResp.elapsed_seconds:F1}s! Report length: {statusResp.text?.Length ?? 0} chars.");
                    return statusResp;
                }
                else if (statusResp.status.Equals("FAILED", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"[DEEP-RESEARCH] Failed: {statusResp.error}");
                else
                    onProgress($"[DEEP-RESEARCH-POLL] Deep Research in progress... Elapsed: {statusResp.elapsed_seconds:F0}s (Checking internet sources & aggregating data...)");
            }

            throw new TimeoutException($"[DEEP-RESEARCH] Research timed out after {timeoutSeconds}s.");
        }

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
            catch { return false; }
        }

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
            catch { return false; }
        }

        public async Task<List<AvailableModelInfo>> ListAvailableModelsAsync()
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/models";
            var response = await _httpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"List models error ({response.StatusCode}): {content}");

            var models = JsonSerializer.Deserialize<List<AvailableModelInfo>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return models ?? new List<AvailableModelInfo>();
        }
    }
}
