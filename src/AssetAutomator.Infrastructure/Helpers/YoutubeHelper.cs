using System;
using System.IO;
using System.Text.RegularExpressions;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Infrastructure.Helpers
{
    public sealed class YoutubeHelper
    {
        public static YoutubeHelper Instance { get; set; } = null!;
        private readonly IConfigService _configService;
        private static readonly Regex VideoIdRegex = new(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^&?\/ ]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RawIdRegex = new(@"^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);

        public YoutubeHelper(IConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            Instance = this;
        }

        public string ExtractVideoId(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "unknown";
            if (url.Length == 11 && RawIdRegex.IsMatch(url))
                return url;
            var match = VideoIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        public string GetOutputDir(string videoId)
        {
            string baseDir = _configService.CurrentSettings.OutputsDir ?? string.Empty;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Outputs");
            return Path.Combine(baseDir, videoId);
        }

        public static string ToSafeTopicSlug(string? topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
                return "untitled-topic";

            var builder = new System.Text.StringBuilder();
            foreach (char character in topic.Trim().ToLowerInvariant())
                builder.Append(char.IsLetterOrDigit(character) || character == '-' ? character : ' ');

            string slug = Regex.Replace(builder.ToString(), @"[\s\-]+", "-").Trim('-');
            return string.IsNullOrWhiteSpace(slug) ? "untitled-topic" : slug;
        }
    }
}
