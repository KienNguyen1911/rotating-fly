using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Infrastructure.Helpers;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service for interacting with the Gemini WebAPI REST Server (Modules/Gemini-API-2.0.0).
    /// Handles listing gems, sending chat prompts (with Deep Research & Gem selection), and health checks.
    /// </summary>
    public class GeminiApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly PythonServerManager? _pythonServerManager;
        private readonly IConfigService _configService;

        // ─────────────────────────────────────────────────────
        //  Model Mapping: C# UI names → Python model IDs
        //  Mirrors: Modules/Gemini-API-2.0.0/src/gemini_webapi/constants.py Model enum
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the model name to pass to the Python Gemini API.
        /// Model names come directly from the UI dropdown (9 models from constants.py).
        /// If already a valid "gemini-*" name, returns as-is (idempotent).
        /// </summary>
        public static string ResolveModelName(string? modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName) && modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                return modelName.Trim();

            // Fallback: default to flash
            return "gemini-3-flash";
        }

        /// <summary>
        /// Converts a GemOptionItem (UI model) to a GemModel (API model).
        /// Used by PipelineOrchestrator to bridge UI selection with API calls.
        /// </summary>
        public static GemModel? ConvertFromGemOption(AssetAutomator.Core.Models.GemOptionItem? option)
        {
            if (option == null) return null;
            return new GemModel
            {
                id = option.Id,
                name = option.Name
            };
        }

        public GeminiApiService(IConfigService configService, PythonServerManager? pythonServerManager = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _baseUrl = (_configService.CurrentSettings.GeminiApiBaseUrl ?? "http://localhost:8000").TrimEnd('/');
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _pythonServerManager = pythonServerManager;
        }

        /// <summary>
        /// Verifies server health and launches Python background server if not running.
        /// </summary>
        public async Task<bool> EnsureConnectedAsync()
        {
            if (_pythonServerManager == null)
            {
                // Without a PythonServerManager, just check HTTP health.
                try
                {
                    var response = await _httpClient.GetAsync($"{_baseUrl}/api/health");
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
            var (success, _) = await _pythonServerManager.EnsureServerRunningAsync(_baseUrl);
            return success;
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
        /// Sends a chat prompt to Gemini WebAPI.
        /// </summary>
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

            // Model name is already the full Gemini model name (e.g. "gemini-3-pro")
            string resolvedModel = ResolveModelName(model);

            // ── MODEL LOGGING ──
            bool isThinking = resolvedModel.Contains("pro", StringComparison.OrdinalIgnoreCase);
            string thinkingTag = isThinking ? $"🧠 THINKING (Pro)" : $"📄 STANDARD";
            System.Diagnostics.Debug.WriteLine($"[GEMINI-API] ▶ SendChat: model={resolvedModel} | {thinkingTag} | gem={gemId ?? "default"} | deepResearch={deepResearch}");
            Console.WriteLine($"[GEMINI-API] ▶ SendChat: model={resolvedModel} | {thinkingTag} | gem={gemId ?? "default"}");

            var payload = new Dictionary<string, object>
            {
                { "message", message },
                { "deep_research", deepResearch },
                { "temporary", temporary },
                { "model", resolvedModel }
            };

            if (!string.IsNullOrWhiteSpace(gemId))
            {
                payload["gem_id"] = gemId;
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

            // ── MODEL LOGGING cho Deep Research ──
            bool isThinking = model != null && model.Contains("thinking", StringComparison.OrdinalIgnoreCase);
            string tier = model != null && model.Contains("advanced", StringComparison.OrdinalIgnoreCase) ? "Advanced" :
                          model != null && model.Contains("plus", StringComparison.OrdinalIgnoreCase) ? "Plus" : "Basic";
            string thinkingTag = isThinking ? $"🧠 THINKING ({tier})" : $"📄 STANDARD ({tier})";
            System.Diagnostics.Debug.WriteLine($"[GEMINI-API] ▶ StartDeepResearch: model={model ?? "default"} | {thinkingTag} | gem={gemId ?? "default"}");
            Console.WriteLine($"[GEMINI-API] ▶ StartDeepResearch: model={model ?? "default"} | {thinkingTag} | gem={gemId ?? "default"}");

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

        /// <summary>
        /// Fetches the list of available Gemini models from the Python server's dynamic model registry.
        /// Returns models with thinking status, capacity, and availability info.
        /// This is the GROUND TRUTH for what models Gemini actually supports on your account.
        /// </summary>
        public async Task<List<AvailableModelInfo>> ListAvailableModelsAsync()
        {
            await EnsureConnectedAsync();
            string url = $"{_baseUrl}/api/models";
            var response = await _httpClient.GetAsync(url);
            string content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"List models error ({response.StatusCode}): {content}");
            }

            var models = JsonSerializer.Deserialize<List<AvailableModelInfo>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return models ?? new List<AvailableModelInfo>();
        }
    }
}