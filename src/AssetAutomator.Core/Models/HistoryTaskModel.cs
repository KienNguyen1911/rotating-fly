using System;
using System.IO;
using System.Text.RegularExpressions;

namespace AssetAutomator.Core.Models
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
        public bool StepSrt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SelectedProfile { get; set; } = string.Empty;
        public string Logs { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Step1Status { get; set; } = "Pending";
        public string Step2Status { get; set; } = "Pending";
        public string Step3Status { get; set; } = "Pending";
        public string Step4Status { get; set; } = "Pending";
        public string Step5Status { get; set; } = "Pending";
        public string StepSrtStatus { get; set; } = "Pending";

        public string CreatedAtFormatted => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss");

        private static readonly Regex VideoIdRegex = new Regex(
            @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/ ]{11})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string VideoId
        {
            get
            {
                if (string.IsNullOrWhiteSpace(VideoUrl)) return "unknown";
                var match = VideoIdRegex.Match(VideoUrl);
                return match.Success ? match.Groups[1].Value : "unknown";
            }
        }

        public string OutputDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Outputs", VideoId);
    }
}
