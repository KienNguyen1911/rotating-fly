using System;
using System.Diagnostics;
using System.Collections.Generic;
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
                    logTask(task, "[WARNING] No closed caption tracks found for this video.");
                    // Fallback to Python transcript API
                    var fallbackTranscript = await FallbackToPythonAsync(task.VideoId, task.TargetLanguage, proxyList, task, logTask);
                    if (!string.IsNullOrWhiteSpace(fallbackTranscript))
                    {
                        transcriptText = fallbackTranscript;
                        // Save fallback transcript
                        string outputPath = Path.Combine(task.OutputDir, "transcript.txt");
                        await File.WriteAllTextAsync(outputPath, transcriptText);
                        logTask(task, $"[INFO] Saved fallback transcript to: {outputPath}");
                    }
                    return transcriptText;
                }

                // Try to find target language or default tracks
                logTask(task, "Selecting best caption track...");
                var trackInfo = SelectBestTrack(trackManifest, task.TargetLanguage);

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
        private ClosedCaptionTrackInfo? SelectBestTrack(ClosedCaptionManifest manifest, string targetLanguage)
        {
            // Determine language code from targetLanguage
            string targetLangCode = "en";
            if (targetLanguage.Contains(" - "))
                targetLangCode = targetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[1].Trim();
            else if (targetLanguage.StartsWith("Viet", StringComparison.OrdinalIgnoreCase))
                targetLangCode = "vi";

            var track = manifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals(targetLangCode, StringComparison.OrdinalIgnoreCase))
                ?? manifest.Tracks.FirstOrDefault(t => t.Language.Name.Contains(
                    targetLanguage.Contains(" - ")
                        ? targetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim()
                        : targetLanguage,
                    StringComparison.OrdinalIgnoreCase))
                ?? manifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("vi", StringComparison.OrdinalIgnoreCase))
                ?? manifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("en", StringComparison.OrdinalIgnoreCase))
                ?? manifest.Tracks.FirstOrDefault();

            return track;
        }

        private async Task<string?> FallbackToPythonAsync(string videoId, string targetLanguage, System.Collections.Generic.List<string?> proxyList, AutomationTask task, Action<AutomationTask, string> logTask)
        {
            logTask(task, "[FALLBACK] Running python fallback script for transcript extraction...");
            
            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "fallback_transcript.py");
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "fallback_transcript.py");
            }

            if (!File.Exists(scriptPath))
            {
                logTask(task, $"[ERROR] Fallback script not found at: {scriptPath}");
                return null;
            }

            // Choose first available proxy for python fallback, if any
            var proxy = proxyList.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            var args = new System.Collections.Generic.List<string>();
            args.Add($"\"{scriptPath}\"");
            args.Add("--video-id");
            args.Add($"\"{videoId}\"");
            args.Add("--lang");
            args.Add($"\"{targetLanguage}\"");
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                args.Add("--proxy");
                args.Add($"\"{proxy}\"");
            }

            string pythonExe = ResolvePythonExecutable(task, logTask);

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    logTask(task, "[ERROR] Failed to start python fallback process.");
                    return null;
                }
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    logTask(task, $"[PYTHON] {error.Trim()}");
                }

                if (process.ExitCode != 0)
                {
                    logTask(task, $"[ERROR] Python fallback exited with code {process.ExitCode}");
                    return null;
                }
                return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] Exception while running python fallback: {ex.Message}");
                return null;
            }
        }

        private string ResolvePythonExecutable(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            string[] candidatePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PythonEmbed", "python.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "PythonEmbed", "python.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "PythonEmbed", "python.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "PythonEmbed", "python.exe")
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    logTask(task, $"[PYTHON] Using embedded Python runtime: {path}");
                    return path;
                }
            }

            logTask(task, "[PYTHON] Embedded Python not found. Falling back to system 'python' command.");
            return "python";
        }
    }
}
