using System.Collections.Generic;

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
        public List<string> images { get; set; } = new List<string>();
        public bool deep_research_completed { get; set; }
    }

    /// <summary>
    /// Streaming chat response result. Aggregates text + thoughts from SSE events
    /// emitted by the Python server's <c>/api/chat/stream-extended</c> endpoint.
    /// Mirrors the behavior of <c>generate_content_stream</c> in
    /// <c>test_gem_and_thinking.py</c>.
    /// </summary>
    public class GeminiChatStreamResult
    {
        public string SessionId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string Thoughts { get; set; } = string.Empty;
        public List<GeminiChatResponseImage> Images { get; set; } = new();
        public bool Completed { get; set; }
        public string? Error { get; set; }
    }

    public class GeminiChatResponseImage
    {
        public string url { get; set; } = string.Empty;
        public string? title { get; set; }
        public string? alt { get; set; }
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
    /// Represents a model returned by Gemini's dynamic model registry (GET /api/models).
    /// Mirrors ModelInfoResponse in server.py.
    /// </summary>
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
}