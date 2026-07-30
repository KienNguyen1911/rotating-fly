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