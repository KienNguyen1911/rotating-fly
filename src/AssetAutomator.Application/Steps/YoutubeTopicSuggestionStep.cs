using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Pipeline Step 1: Suggests video topics based on a YouTube channel URL.
    /// Uses Gemini API to analyze the channel's niche/audience and propose relevant topics.
    /// Supports both YouTube channel URLs (youtube.com/@handle or /channel/ID) and
    /// direct topic descriptions as fallback.
    /// </summary>
    public class YoutubeTopicSuggestionStep
    {
        private readonly GeminiApiService _geminiApiService;

        public YoutubeTopicSuggestionStep(GeminiApiService geminiApiService)
        {
            _geminiApiService = geminiApiService;
        }

        /// <summary>
        /// Analyzes a YouTube channel URL and returns suggested video topics.
        /// </summary>
        /// <param name="channelUrl">YouTube channel URL (e.g. https://youtube.com/@ChannelName)</param>
        /// <param name="scriptwriterGemId">Gemini Gem ID to use for analysis</param>
        /// <param name="model">Gemini model name</param>
        /// <param name="logAction">Logging callback</param>
        /// <returns>Parsed topic suggestions or null on failure</returns>
        public async Task<YoutubeTopicSuggestionResponse?> SuggestTopicsAsync(
            string channelUrl,
            string? scriptwriterGemId,
            string? model,
            Action<string> logAction)
        {
            logAction("[STEP 1] 🔍 Analyzing YouTube channel for topic suggestions...");

            string resolvedModel = GeminiApiService.ResolveModelName(model);
            bool isChannelUrl = IsYouTubeChannelUrl(channelUrl);

            string prompt = isChannelUrl
                ? BuildChannelAnalysisPrompt(channelUrl)
                : BuildTopicRefinementPrompt(channelUrl);

            logAction($"[STEP 1] 📤 Sending topic suggestion prompt to Gemini (model: {resolvedModel})...");

            try
            {
                var response = await _geminiApiService.SendChatAsync(
                    message: prompt,
                    gemId: scriptwriterGemId,
                    model: resolvedModel,
                    deepResearch: false,
                    temporary: true
                );

                if (string.IsNullOrWhiteSpace(response.text))
                {
                    logAction("[STEP 1] ⚠️ Gemini returned empty response for topic suggestions.");
                    return null;
                }

                logAction($"[STEP 1] 📥 Response received ({response.text.Length} chars). Parsing topics...");

                // Extract JSON from response
                string jsonText = ExtractJsonContent(response.text);
                var suggestions = ParseTopicResponse(jsonText, logAction);

                if (suggestions != null && suggestions.SuggestedTopics.Count > 0)
                {
                    logAction($"[STEP 1] ✅ Extracted {suggestions.SuggestedTopics.Count} suggested topics from channel '{suggestions.ChannelName}'.");
                    return suggestions;
                }

                // Fallback: Try to parse raw text lines as topics
                logAction("[STEP 1] ⚠️ JSON parsing failed. Attempting fallback text parsing...");
                var fallbackTopics = ParseTopicsFromText(response.text);
                if (fallbackTopics.Count > 0)
                {
                    var fallback = new YoutubeTopicSuggestionResponse
                    {
                        ChannelName = ExtractChannelName(channelUrl),
                        ChannelNiche = "General",
                        SuggestedTopics = fallbackTopics
                    };
                    logAction($"[STEP 1] ✅ Fallback: extracted {fallbackTopics.Count} topics from text.");
                    return fallback;
                }

                logAction("[STEP 1] ❌ Could not extract any topics from Gemini response.");
                return null;
            }
            catch (Exception ex)
            {
                logAction($"[STEP 1] ❌ Topic suggestion failed: {ex.Message}");
                return null;
            }
        }

        #region Prompt Building

        private static string BuildChannelAnalysisPrompt(string channelUrl)
        {
            return $@"Analyze this YouTube channel: {channelUrl}

Based on the channel's niche, audience, and content style, suggest 8-12 compelling video topics that would perform well on this channel. For each topic, consider:
- Current trends in the channel's niche
- Evergreen content that drives consistent views
- Topics that spark discussion and engagement
- Content that complements the channel's existing videos

RETURN ONLY valid JSON in this EXACT format (no markdown, no extra text):
{{
  ""channel_name"": ""Channel Name"",
  ""channel_niche"": ""e.g. Psychology, Tech Reviews, Gaming, Education"",
  ""suggested_topics"": [
    {{
      ""title"": ""Topic Title"",
      ""description"": ""Brief 1-2 sentence description of what the video would cover"",
      ""category"": ""e.g. Tutorial, Analysis, Commentary, Listicle"",
      ""estimated_duration"": ""8-12 phút""
    }}
  ]
}}

IMPORTANT: Output raw JSON only. No markdown code blocks, no explanations.";
        }

        private static string BuildTopicRefinementPrompt(string topicDescription)
        {
            return $@"I want to create a video about: {topicDescription}

Suggest 6-10 related video topic variations and angles that would make great YouTube videos. Consider different formats (listicle, deep dive, tutorial, commentary, etc.).

RETURN ONLY valid JSON in this EXACT format (no markdown, no extra text):
{{
  ""channel_name"": ""Custom Topic"",
  ""channel_niche"": ""auto-detected"",
  ""suggested_topics"": [
    {{
      ""title"": ""Topic Title"",
      ""description"": ""Brief 1-2 sentence description"",
      ""category"": ""e.g. Tutorial, Analysis, Commentary, Listicle"",
      ""estimated_duration"": ""8-12 phút""
    }}
  ]
}}

IMPORTANT: Output raw JSON only. No markdown code blocks, no explanations.";
        }

        #endregion

        #region Parsing

        /// <summary>
        /// Extracts JSON content from Gemini response text, handling markdown code blocks.
        /// </summary>
        private static string ExtractJsonContent(string text)
        {
            // Try markdown code block first
            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // Find JSON object boundaries
            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return text.Trim();
        }

        private static YoutubeTopicSuggestionResponse? ParseTopicResponse(string jsonText, Action<string> log)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<YoutubeTopicSuggestionResponse>(jsonText, options);

                if (result == null || result.SuggestedTopics == null || result.SuggestedTopics.Count == 0)
                {
                    log("[STEP 1] ⚠️ Parsed JSON but got 0 topics.");
                    return null;
                }

                // Clean up topic titles
                foreach (var topic in result.SuggestedTopics)
                {
                    topic.Title = CleanTopicTitle(topic.Title);
                }

                return result;
            }
            catch (JsonException ex)
            {
                log($"[STEP 1] ⚠️ JSON parse error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fallback parser: extracts numbered/bulleted topics from raw text.
        /// </summary>
        private static List<SuggestedTopic> ParseTopicsFromText(string text)
        {
            var topics = new List<SuggestedTopic>();

            // Match numbered topics: "1. Title - Description" or "1) Title: Description"
            var numberedPattern = new Regex(
                @"(?:^|\n)\s*(?:\d+[\.\)]\s*|[\-\*\•]\s+)(?<title>.+?)(?:\s*[-–:]\s*(?<desc>.+?))?(?=\n\s*(?:\d+[\.\)]\s*|[\-\*\•]\s+)|\n\s*$|$)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);

            var matches = numberedPattern.Matches(text);
            foreach (Match m in matches)
            {
                string title = CleanTopicTitle(m.Groups["title"].Value);
                string desc = m.Groups["desc"].Success
                    ? m.Groups["desc"].Value.Trim()
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(title) && title.Length > 5)
                {
                    topics.Add(new SuggestedTopic
                    {
                        Title = title,
                        Description = desc,
                        Category = "General",
                        EstimatedDuration = "8-12 phút"
                    });
                }
            }

            return topics.Take(15).ToList();
        }

        private static string CleanTopicTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;

            // Remove leading numbering
            title = Regex.Replace(title.Trim(), @"^\d+[\.\)]\s*", "");
            // Remove markdown bold/italic
            title = Regex.Replace(title, @"\*{1,3}(.+?)\*{1,3}", "$1");
            // Remove quotes wrapping
            title = title.Trim('"', '\'', '「', '」');

            return string.IsNullOrWhiteSpace(title) ? "Untitled Topic" : title;
        }

        #endregion

        #region URL Helpers

        private static bool IsYouTubeChannelUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return url.Contains("youtube.com/@", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("youtube.com/channel/", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("youtube.com/c/", StringComparison.OrdinalIgnoreCase) ||
                   url.Contains("youtube.com/user/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractChannelName(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Unknown Channel";

            // Try @handle
            var atMatch = Regex.Match(url, @"youtube\.com/@([^/?&]+)");
            if (atMatch.Success) return atMatch.Groups[1].Value;

            // Try /c/ or /user/
            var cMatch = Regex.Match(url, @"youtube\.com/(?:c|user)/([^/?&]+)");
            if (cMatch.Success) return cMatch.Groups[1].Value;

            return url;
        }

        #endregion
    }
}