using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Step 1: Downloads the YouTube video thumbnail via direct HTTP request.
    /// Tries maxresdefault first, falls back to hqdefault.
    /// </summary>
    public class ThumbnailDownloadStep
    {
        public async Task ExecuteAsync(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 1] Starting download thumbnail...");
            string targetUrl = $"https://img.youtube.com/vi/{task.VideoId}/maxresdefault.jpg";
            string fallbackUrl = $"https://img.youtube.com/vi/{task.VideoId}/hqdefault.jpg";
            string outputPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");

            using var httpClient = new HttpClient();
            try
            {
                logTask(task, $"[STEP 1] Trying to download maxresdefault: {targetUrl}");
                var response = await httpClient.GetAsync(targetUrl);
                if (!response.IsSuccessStatusCode)
                {
                    logTask(task, $"[STEP 1] Maxresdefault not available. Downloading fallback: {fallbackUrl}");
                    response = await httpClient.GetAsync(fallbackUrl);
                }

                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(outputPath, bytes);
                    logTask(task, $"[STEP 1] Success! Saved thumbnail to: {outputPath}");
                }
                else
                {
                    logTask(task, $"[STEP 1] [ERROR] Failed to retrieve thumbnail image from YouTube API.");
                }
            }
            catch (Exception ex)
            {
                logTask(task, $"[STEP 1] [ERROR] Thumbnail download failed: {ex.Message}");
            }
        }
    }
}