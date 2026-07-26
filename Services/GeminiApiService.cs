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

    /// <summary>
    /// Service for interacting with the Gemini WebAPI REST Server (Modules/Gemini-API-2.0.0).
    /// Handles listing gems, sending chat prompts (with Deep Research & Gem selection), and health checks.
    /// </summary>
    public class GeminiApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

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
            bool deepResearch = false,
            string? sessionId = null,
            bool temporary = false)
        {
            await EnsureConnectedAsync();

            var payload = new Dictionary<string, object>
            {
                { "message", message },
                { "deep_research", deepResearch },
                { "temporary", temporary }
            };

            if (!string.IsNullOrWhiteSpace(gemId))
            {
                payload["gem_id"] = gemId;
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                payload["session_id"] = sessionId;
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
