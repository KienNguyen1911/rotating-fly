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

        public string VideoId => YoutubeHelper.ExtractVideoId(VideoUrl);

        public string OutputDir => YoutubeHelper.GetOutputDir(VideoId);
    }
}
