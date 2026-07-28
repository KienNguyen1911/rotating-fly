using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AssetAutomator.Models
{
    /// <summary>
    /// Response model from Gemini when asked to suggest video topics for a YouTube channel.
    /// </summary>
    public class YoutubeTopicSuggestionResponse
    {
        [JsonPropertyName("channel_name")]
        public string ChannelName { get; set; } = string.Empty;

        [JsonPropertyName("channel_niche")]
        public string ChannelNiche { get; set; } = string.Empty;

        [JsonPropertyName("suggested_topics")]
        public List<SuggestedTopic> SuggestedTopics { get; set; } = new();
    }

    public class SuggestedTopic
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("estimated_duration")]
        public string EstimatedDuration { get; set; } = "8-12 phút";

        /// <summary>User has selected this topic for pipeline execution.</summary>
        [JsonIgnore]
        public bool IsSelected { get; set; }
    }
}
