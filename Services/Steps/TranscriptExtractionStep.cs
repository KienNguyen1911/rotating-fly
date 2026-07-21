using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Playwright;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace AssetAutomator
{
    /// <summary>
    /// Step 2: Extracts video transcript using YoutubeExplode library.
    /// Falls back through multiple language tracks to find the best match.
    /// </summary>
    public class TranscriptExtractionStep
    {
        public async Task<string?> ExecuteAsync(AutomationTask task, IBrowserContext context, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 2] Starting transcript extraction via YoutubeExplode...");
            string? transcriptText = null;
            try
            {
                // 1. Try manual proxies first
                logTask(task, "[PROXY] Loading manual proxies...");
                var manualProxies = ProxyHelper.LoadProxiesFromFile(ConfigService.CurrentSettings.ManualProxiesFilePath);
                var activeProxies = await ProxyHelper.GetAliveProxiesAsync(manualProxies, msg => logTask(task, msg), timeoutSeconds: 10);

                // 2. If no manual proxies are active, fallback to free proxies
                if (activeProxies.Count == 0)
                {
                    logTask(task, "[PROXY] No active manual proxies. Falling back to free proxies...");
                    var freeProxies = ProxyHelper.LoadProxiesFromFile(ConfigService.CurrentSettings.ProxiesFilePath);
                    activeProxies = await ProxyHelper.GetAliveProxiesAsync(freeProxies, msg => logTask(task, msg), timeoutSeconds: 10);
                }

                var proxyList = new System.Collections.Generic.List<string?>(activeProxies);
                // Add null at the end as direct connection fallback
                proxyList.Add(null);

                YoutubeClient? youtube = null;
                YoutubeExplode.Videos.ClosedCaptions.ClosedCaptionManifest? trackManifest = null;

                foreach (var proxy in proxyList)
                {
                    try
                    {
                        HttpClient httpClient;
                        if (proxy != null)
                        {
                            logTask(task, $"[PROXY] Trying YouTube request via proxy: {proxy}");
                            var webProxy = ProxyHelper.ParseProxy(proxy);
                            if (webProxy == null)
                            {
                                logTask(task, $"[WARNING] Failed to parse proxy string: {proxy}");
                                continue;
                            }
                            var handler = new HttpClientHandler
                            {
                                Proxy = webProxy,
                                UseProxy = true
                            };
                            httpClient = new HttpClient(handler);
                        }
                        else
                        {
                            logTask(task, "[PROXY] Trying YouTube request via direct connection...");
                            httpClient = new HttpClient();
                        }

                        httpClient.Timeout = TimeSpan.FromSeconds(15);
                        var clientInstance = new YoutubeClient(httpClient);

                        trackManifest = await clientInstance.Videos.ClosedCaptions.GetManifestAsync(task.VideoId);
                        if (trackManifest != null)
                        {
                            youtube = clientInstance;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logTask(task, $"[WARNING] Proxy failed or connection error: {proxy}. Detail: {ex.Message}");
                    }
                }

                if (youtube == null || trackManifest == null)
                {
                    logTask(task, "[ERROR] All proxies and direct connection failed to fetch Closed Caption Manifest.");
                    return null;
                }

                if (trackManifest == null || !trackManifest.Tracks.Any())
                {
                    logTask(task, "[ERROR] No closed caption tracks found for this video.");
                    return null;
                }

                // Try to find target language or default tracks
                logTask(task, "Selecting best caption track...");
                string targetLangCode = "en";
                if (task.TargetLanguage.Contains(" - "))
                {
                    targetLangCode = task.TargetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[1].Trim();
                }
                else if (task.TargetLanguage.StartsWith("Viet", StringComparison.OrdinalIgnoreCase))
                {
                    targetLangCode = "vi";
                }

                var trackInfo = trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals(targetLangCode, StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Name.Contains(task.TargetLanguage.Contains(" - ") ? task.TargetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim() : task.TargetLanguage, StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("vi", StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("en", StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault();

                if (trackInfo == null)
                {
                    logTask(task, "[ERROR] Could not find any suitable caption track.");
                    return null;
                }

                logTask(task, $"Selected caption track: {trackInfo.Language.Name} ({trackInfo.Language.Code})");
                var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);

                var segments = track.Captions.Select(c => c.Text);
                transcriptText = string.Join(" ", segments);

                if (!string.IsNullOrWhiteSpace(transcriptText))
                {
                    logTask(task, $"Success! Retrieved transcript length: {transcriptText.Length} characters.");
                    string outputPath = Path.Combine(task.OutputDir, "transcript.txt");
                    await File.WriteAllTextAsync(outputPath, transcriptText);
                    logTask(task, $"Saved transcript text to: {outputPath}");
                }
                else
                {
                    logTask(task, "[ERROR] Transcript was empty.");
                }
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] Transcript extraction failed: {ex.Message}");
                throw;
            }

            return transcriptText;
        }
    }
}
