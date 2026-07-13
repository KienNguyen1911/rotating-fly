using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AutoCreateImage
{
    public class HistoryTaskModel
    {
        public Guid Id { get; set; }
        public string VideoUrl { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string VoiceId { get; set; } = string.Empty;
        public bool Step1 { get; set; }
        public bool Step2 { get; set; }
        public bool Step3 { get; set; }
        public bool Step4 { get; set; }
        public bool Step5 { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SelectedProfile { get; set; } = string.Empty;
        public string Logs { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }

        public string CreatedAtFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

        public string VideoId
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VideoUrl)) return "unknown";
                if (VideoUrl.Length == 11 && Regex.IsMatch(VideoUrl, @"^[a-zA-Z0-9_-]{11}$"))
                {
                    return VideoUrl;
                }
                var match = Regex.Match(VideoUrl, @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/ ]{11})", RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : "unknown";
            }
        }

        public string OutputDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Outputs", VideoId);
    }
}
