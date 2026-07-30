using System.Collections.Generic;

namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// API response model for shared voices listing.
    /// Extracted from VoiceSelectorWindow.xaml.cs.
    /// </summary>
    public class SharedVoicesResponse
    {
        public List<SharedVoiceInfo> voices { get; set; } = new();
        public bool has_more { get; set; }
        public string? last_sort_id { get; set; }
    }

    /// <summary>
    /// Represents a single shared voice entry from the AI84 API.
    /// </summary>
    public class SharedVoiceInfo
    {
        public string voice_id { get; set; } = string.Empty;
        public string name { get; set; } = string.Empty;
        public string category { get; set; } = string.Empty;
        public string gender { get; set; } = string.Empty;
        public string language { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
    }
}