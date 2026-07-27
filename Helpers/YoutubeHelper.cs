using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetAutomator
{
    /// <summary>
    /// Shared helper for YouTube-related URL parsing and output directory resolution.
    /// Eliminates duplicate ExtractVideoId and OutputDir logic across AutomationTask and HistoryTaskModel.
    /// </summary>
    public static class YoutubeHelper
    {
        private static readonly Regex VideoIdRegex = new Regex(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/ ]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex RawIdRegex = new Regex(
            @"^[a-zA-Z0-9_-]{11}$",
            RegexOptions.Compiled);

        /// <summary>
        /// Extracts the 11-character YouTube video ID from a URL or raw ID string.
        /// Returns "unknown" if the input is null, empty, or cannot be parsed.
        /// </summary>
        public static string ExtractVideoId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "unknown";

            // Direct 11-char ID
            if (url.Length == 11 && RawIdRegex.IsMatch(url))
            {
                return url;
            }

            // URL pattern match
            var match = VideoIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        /// <summary>
        /// Resolves the output directory for a given video ID using current settings.
        /// Falls back to Desktop/Outputs if no OutputsDir is configured.
        /// </summary>
        public static string GetOutputDir(string videoId)
        {
            string baseDir = ConfigService.CurrentSettings.OutputsDir;
            if (string.IsNullOrEmpty(baseDir))
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                baseDir = Path.Combine(desktopPath, "Outputs");
            }
            return Path.Combine(baseDir, videoId);
        }

        /// <summary>
        /// Converts a raw topic string to a clean, lowercase hyphen-separated folder slug.
        /// Example: "Nighttime Loneliness & FOMO" -> "nighttime-loneliness-fomo"
        /// </summary>
        public static string ToSafeTopicSlug(string? topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return "untitled-topic";

            string text = topic.Trim().ToLowerInvariant();

            var sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            string slug = Regex.Replace(sb.ToString(), @"[\s\-]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "untitled-topic" : slug;
        }
    }
}
