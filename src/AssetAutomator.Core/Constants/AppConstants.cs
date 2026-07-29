using System.Collections.Generic;

namespace AssetAutomator.Core.Constants
{
    public static class AppConstants
    {
        public static readonly List<string> Languages = new List<string>
        {
            "Vietnamese", "English", "French", "German", "Spanish",
            "Italian", "Portuguese", "Russian", "Japanese", "Korean",
            "Chinese", "Thai"
        };

        public static readonly Dictionary<string, string> LanguageCodes = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            { "vi", "Vietnamese" }, { "en", "English" }, { "fr", "French" },
            { "de", "German" }, { "es", "Spanish" }, { "it", "Italian" },
            { "pt", "Portuguese" }, { "ru", "Russian" }, { "ja", "Japanese" },
            { "ko", "Korean" }, { "zh", "Chinese" }, { "th", "Thai" }
        };
    }

    public static class LanguageHelper
    {
        public static string FormatLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            if (AppConstants.LanguageCodes.TryGetValue(code.Trim(), out string? name))
            {
                return $"{name} - {code.ToLower()}";
            }
            return $"{code.ToUpper()} - {code.ToLower()}";
        }
    }

    /// <summary>
    /// Shared helper for YouTube-related URL parsing and output directory resolution.
    /// Lives in Core to avoid circular dependencies between Infrastructure and Application.
    /// </summary>
    public static class YoutubeHelper
    {
        private static readonly System.Text.RegularExpressions.Regex VideoIdRegex = new System.Text.RegularExpressions.Regex(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/ ]{11})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex RawIdRegex = new System.Text.RegularExpressions.Regex(
            @"^[a-zA-Z0-9_-]{11}$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string ExtractVideoId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "unknown";
            if (url.Length == 11 && RawIdRegex.IsMatch(url)) return url;
            var match = VideoIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        public static string ToSafeTopicSlug(string? topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return "untitled-topic";
            string text = topic.Trim().ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                    sb.Append(c);
                else
                    sb.Append(' ');
            }
            string slug = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"[\s\-]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "untitled-topic" : slug;
        }
    }
}
