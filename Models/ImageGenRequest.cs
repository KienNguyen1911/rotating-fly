using System;

namespace AssetAutomator
{
    /// <summary>
    /// Represents a request in the image generation pool queue.
    /// Extracted from MainWindow.xaml.cs for cleaner separation.
    /// </summary>
    public class ImageGenRequest
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public AutomationTask Task { get; set; } = null!;
        public System.Threading.Tasks.TaskCompletionSource<bool> Tcs { get; set; } = new System.Threading.Tasks.TaskCompletionSource<bool>();

        /// <summary>
        /// Current status: "Waiting", "Processing", "Done", "Failed"
        /// </summary>
        public string Status { get; set; } = "Waiting";

        public DateTime EnqueuedAt { get; set; } = DateTime.Now;
        public string TaskId => Task?.Id.ToString().Substring(0, 8) ?? string.Empty;
        public string VideoId => Task?.VideoId ?? string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string CreatedTimeFormatted => EnqueuedAt.ToString("HH:mm:ss");
        public string FinishedTimeFormatted => FinishedAt?.ToString("HH:mm:ss") ?? string.Empty;
    }
}
