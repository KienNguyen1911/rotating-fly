using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Step 4: Generates voiceover via AI84 TTS API (async job + polling)
    /// and generates SRT subtitles via Whisper API.
    ///
    /// All AI84 HTTP traffic goes through <see cref="IHttpClientFactory"/> named clients
    /// registered in <c>App.xaml.cs</c>:
    ///   • <see cref="Ai84HttpClientName"/>      → standard pipeline (lookup/submit/download).
    ///   • <see cref="Ai84PollingHttpClientName"/>→ long-polling pipeline (job status).
    /// This ensures every request has a sane timeout, retry, and circuit-breaker — instead
    /// of the bare <c>new HttpClient()</c> with its 100s default that previously let one
    /// upstream hiccup freeze the whole pipeline.
    /// </summary>
    public class VoiceoverGenerationStep
    {
        /// <summary>DI name for the standard AI84 HttpClient (with default resilience pipeline).</summary>
        public const string Ai84HttpClientName = "ai84";

        /// <summary>DI name for the AI84 long-polling HttpClient (no circuit breaker).</summary>
        public const string Ai84PollingHttpClientName = "ai84-polling";

        private readonly IConfigService _configService;
        private readonly IHttpClientFactory _httpClientFactory;

        public VoiceoverGenerationStep(IConfigService configService, IHttpClientFactory httpClientFactory)
        {
            _configService = configService;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Backwards-compatible shim so legacy callers that still construct this step
        /// manually (or tests that don't register an <see cref="IHttpClientFactory"/>)
        /// keep working. Falls back to a raw <see cref="HttpClient"/> with a sensible
        /// outer timeout, **without** retry / circuit-breaker — so production code should
        /// always use the DI path.
        /// </summary>
        internal VoiceoverGenerationStep(IConfigService configService) : this(configService, new NoResilienceHttpClientFactory())
        {
        }

        public async Task ExecuteAsync(
            string voiceId,
            string outputDir,
            string scriptText,
            string videoId,
            AutomationTask task,
            string apiKey,
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[VOICEOVER] Starting Asynchronous Voiceover creation via AI84 API...");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("AI84 API Key is empty. Please enter your API Key in the top toolbar.");
            }

            // Use the dedicated polling client for job submission so a transient blip
            // doesn't fail the submit and leave an orphan job on the upstream.
            var submitClient = _httpClientFactory.CreateClient(Ai84HttpClientName);
            submitClient.DefaultRequestHeaders.Remove("xi-api-key");
            submitClient.DefaultRequestHeaders.TryAddWithoutValidation("xi-api-key", apiKey);

            bool withTranscript = (task.SrtMethod == 1);

            var requestBody = new
            {
                text = scriptText,
                voice_id = voiceId,
                model_id = "eleven_v3",
                with_transcript = withTranscript
            };

            string json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // Submit TTS job
            string jobId = await SubmitTtsJobAsync(submitClient, voiceId, content, task, logTask);

            // Poll for completion — use the long-polling client because the server is
            // expected to be slow but responsive, NOT to be failing.
            var pollingClient = _httpClientFactory.CreateClient(Ai84PollingHttpClientName);
            var (audioUrl, transcriptUrl) = await PollTtsJobAndTranscriptAsync(pollingClient, jobId, apiKey, task, scriptText, withTranscript, logTask);

            // Download audio and SRT (standard client — these are CDN downloads).
            await DownloadAudioAndGenerateSrtAsync(submitClient, audioUrl, transcriptUrl, outputDir, apiKey, task, logTask);
        }

        private async Task<string> SubmitTtsJobAsync(
            HttpClient httpClient, string voiceId, StringContent content,
            AutomationTask task, Action<AutomationTask, string> logTask)
        {
            try
            {
                string submitUrl = $"https://api.ai84.pro/v2/text-to-speech/async?voice_id={Uri.EscapeDataString(voiceId)}";
                logTask(task, $"[VOICEOVER] Creating TTS Job: POST {submitUrl}");
                var response = await httpClient.PostAsync(submitUrl, content);

                string responseContent = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"TTS Job creation failed with status code {response.StatusCode}. Details: {responseContent}");
                }

                using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
                string jobId = doc.RootElement.GetProperty("job_id").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(jobId))
                {
                    throw new Exception("Did not receive a valid job_id from API.");
                }

                logTask(task, $"[VOICEOVER] Job created successfully. Job ID: {jobId}");
                return jobId;
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] Failed to create TTS job: {ex.Message}");
                throw;
            }
        }

        private async Task<(string audioUrl, string? transcriptUrl)> PollTtsJobAndTranscriptAsync(
            HttpClient httpClient, string jobId, string apiKey,
            AutomationTask task, string scriptText, bool withTranscript, Action<AutomationTask, string> logTask)
        {
            string audioUrl = string.Empty;
            string? transcriptUrl = null;
            double duration = 0;
            int attempt = 0;

            while (attempt < 100)
            {
                attempt++;
                await Task.Delay(Delays.VoiceoverDelayMs);

                try
                {
                    string statusUrl = $"https://api.ai84.pro/v2/text-to-speech/async/{jobId}";
                    logTask(task, $"[VOICEOVER] Polling job status (Attempt {attempt}): GET {statusUrl}");

                    using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                    statusRequest.Headers.Add("xi-api-key", apiKey);
                    var statusResponse = await httpClient.SendAsync(statusRequest);

                    string statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        logTask(task, $"[VOICEOVER] [WARNING] Polling failed: {statusResponseContent}. Retrying...");
                        continue;
                    }

                    using var doc = System.Text.Json.JsonDocument.Parse(statusResponseContent);
                    var jobElement = doc.RootElement.GetProperty("job");
                    string status = jobElement.GetProperty("status").GetString() ?? "queued";

                    logTask(task, $"[VOICEOVER] Job status: {status}");

                    if (status.Equals("done", StringComparison.OrdinalIgnoreCase))
                    {
                        audioUrl = ExtractJsonStringProperty(jobElement, "audioUrl", "audio_url", "result") ?? string.Empty;

                        if (string.IsNullOrEmpty(audioUrl))
                        {
                            throw new Exception($"TTS job marked as done but no audio URL found. Response JSON: {statusResponseContent}");
                        }

                        if (jobElement.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out var dur))
                        {
                            duration = dur;
                        }
                        else if (jobElement.TryGetProperty("audio_duration", out var durProp2) && durProp2.TryGetDouble(out var dur2))
                        {
                            duration = dur2;
                        }

                        transcriptUrl = ExtractJsonStringProperty(jobElement, "transcriptUrl", "transcript_url");
                        break;
                    }
                    else if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        string errMsg = ExtractJsonStringProperty(jobElement, "error_message", "errorMessage") ?? "Unknown error";
                        throw new Exception($"TTS Job failed: {errMsg}");
                    }
                }
                catch (Exception ex)
                {
                    logTask(task, $"[ERROR] Polling status failed: {ex.Message}");
                    throw;
                }
            }

            if (string.IsNullOrEmpty(audioUrl))
            {
                throw new Exception("Timed out waiting for TTS job to complete or audio URL was empty.");
            }

            if (withTranscript && string.IsNullOrEmpty(transcriptUrl))
            {
                logTask(task, "[VOICEOVER] Audio is ready, but transcriptUrl is not yet available. Determining polling budget...");
                if (duration <= 0)
                {
                    duration = scriptText.Length / 15.0;
                    if (duration < 10) duration = 10;
                }

                double maxPollingSeconds = duration / 1.5;
                if (maxPollingSeconds < 5) maxPollingSeconds = 5;

                logTask(task, $"[VOICEOVER] Max polling budget for transcriptUrl: {maxPollingSeconds:F1} seconds.");

                var startTime = DateTime.UtcNow;
                int transcriptAttempt = 0;
                while ((DateTime.UtcNow - startTime).TotalSeconds < maxPollingSeconds)
                {
                    transcriptAttempt++;
                    await Task.Delay(Delays.PageRenderDelayMs);

                    try
                    {
                        string statusUrl = $"https://api.ai84.pro/v2/text-to-speech/async/{jobId}";
                        logTask(task, $"[VOICEOVER] Polling for transcriptUrl (Attempt {transcriptAttempt}): GET {statusUrl}");

                        using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                        statusRequest.Headers.Add("xi-api-key", apiKey);
                        var statusResponse = await httpClient.SendAsync(statusRequest);

                        if (statusResponse.IsSuccessStatusCode)
                        {
                            string statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                            using var doc = System.Text.Json.JsonDocument.Parse(statusResponseContent);
                            var jobElement = doc.RootElement.GetProperty("job");
                            transcriptUrl = ExtractJsonStringProperty(jobElement, "transcriptUrl", "transcript_url");
                            if (!string.IsNullOrEmpty(transcriptUrl))
                            {
                                logTask(task, $"[STEP 4] transcriptUrl found: {transcriptUrl}");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logTask(task, $"[WARNING] Failed to poll for transcriptUrl: {ex.Message}");
                    }
                }

                if (string.IsNullOrEmpty(transcriptUrl))
                {
                    logTask(task, "[WARNING] Timed out waiting for transcriptUrl. Will fallback to Whisper API.");
                }
            }

            return (audioUrl, transcriptUrl);
        }

        private async Task DownloadAudioAndGenerateSrtAsync(
            HttpClient httpClient, string audioUrl, string? transcriptUrl, string outputDir, string apiKey,
            AutomationTask task, Action<AutomationTask, string> logTask)
        {
            try
            {
                logTask(task, $"[STEP 4] Downloading generated voiceover from CDN: {audioUrl}");
                var audioResponse = await httpClient.GetAsync(audioUrl);
                if (!audioResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to download audio from CDN. Status: {audioResponse.StatusCode}");
                }

                string downloadPath = Path.Combine(outputDir, "voiceover.mp3");
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await audioResponse.Content.CopyToAsync(fileStream);
                }

                logTask(task, $"[STEP 4] Success! Saved voiceover file to: {downloadPath}");

                if (!task.StepSrt)
                {
                    logTask(task, "[STEP 4] SRT generation is disabled. Skipping subtitle creation.");
                    return;
                }

                task.StepSrtStatus = "Running";
                try
                {
                    if (!string.IsNullOrEmpty(transcriptUrl))
                    {
                        logTask(task, $"[STEP 4] Downloading transcript from: {transcriptUrl}");
                        var transcriptResponse = await httpClient.GetAsync(transcriptUrl);
                        if (transcriptResponse.IsSuccessStatusCode)
                        {
                            string transcriptSrtContent = await transcriptResponse.Content.ReadAsStringAsync();
                            string transcriptSrtPath = Path.Combine(outputDir, "voiceover.srt");
                            await File.WriteAllTextAsync(transcriptSrtPath, transcriptSrtContent, System.Text.Encoding.UTF8);
                            logTask(task, $"[STEP 4] Success! Saved transcript SRT file from AI84 to: {transcriptSrtPath}");
                            task.StepSrtStatus = "Done";
                            return;
                        }
                        else
                        {
                            logTask(task, $"[WARNING] Failed to download transcript from {transcriptUrl}. Falling back to Whisper API...");
                        }
                    }

                    string subtitleApiUrl = _configService.CurrentSettings.SubtitleApiUrl;
                    if (string.IsNullOrWhiteSpace(subtitleApiUrl))
                    {
                        throw new InvalidOperationException("Subtitle API URL is empty or not configured. Cannot generate SRT subtitles.");
                    }

                    logTask(task, $"[STEP 4] Uploading voiceover.mp3 to Whisper SRT API: {subtitleApiUrl}...");
                    using var uploadContent = new MultipartFormDataContent();

                    using var fs = new FileStream(downloadPath, FileMode.Open, FileAccess.Read);
                    using var fileStreamContent = new StreamContent(fs);
                    fileStreamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    uploadContent.Add(fileStreamContent, "file", Path.GetFileName(downloadPath));
                    uploadContent.Add(new StringContent("Auto"), "language");

                    var subResponse = await httpClient.PostAsync(subtitleApiUrl, uploadContent);
                    string subResponseContent = await subResponse.Content.ReadAsStringAsync();

                    if (!subResponse.IsSuccessStatusCode)
                    {
                        throw new Exception($"Whisper SRT API transcription failed with status code {subResponse.StatusCode}. Details: {subResponseContent}");
                    }

                    string srtContent = ParseSrtResponse(subResponseContent);

                    string srtPath = Path.Combine(outputDir, "voiceover.srt");
                    await File.WriteAllTextAsync(srtPath, srtContent, System.Text.Encoding.UTF8);
                    logTask(task, $"[STEP 4] Success! Saved Whisper transcript SRT file to: {srtPath}");
                    task.StepSrtStatus = "Done";
                }
                catch
                {
                    task.StepSrtStatus = "Failed";
                    throw;
                }
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] Failed to download audio file or generate subtitles: {ex.Message}");
                throw;
            }
        }

        public async Task GenerateSrtOnlyAsync(string outputDir, AutomationTask task, Action<AutomationTask, string> logTask)
        {
            try
            {
                string downloadPath = Path.Combine(outputDir, "voiceover.mp3");
                if (!File.Exists(downloadPath))
                {
                    throw new FileNotFoundException("voiceover.mp3 not found. Please run the Voiceover step first or select Voiceover.");
                }

                string subtitleApiUrl = _configService.CurrentSettings.SubtitleApiUrl;
                if (string.IsNullOrWhiteSpace(subtitleApiUrl))
                {
                    throw new InvalidOperationException("Subtitle API URL is empty or not configured. Cannot generate SRT subtitles.");
                }

                using var httpClient = new HttpClient();
                logTask(task, $"[SRT] Uploading voiceover.mp3 to Whisper SRT API: {subtitleApiUrl}...");
                using var uploadContent = new MultipartFormDataContent();

                using var fs = new FileStream(downloadPath, FileMode.Open, FileAccess.Read);
                using var fileStreamContent = new StreamContent(fs);
                fileStreamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                uploadContent.Add(fileStreamContent, "file", Path.GetFileName(downloadPath));
                uploadContent.Add(new StringContent("Auto"), "language");

                var subResponse = await httpClient.PostAsync(subtitleApiUrl, uploadContent);
                string subResponseContent = await subResponse.Content.ReadAsStringAsync();

                if (!subResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Whisper SRT API transcription failed with status code {subResponse.StatusCode}. Details: {subResponseContent}");
                }

                string srtContent = ParseSrtResponse(subResponseContent);
                string srtPath = Path.Combine(outputDir, "voiceover.srt");
                await File.WriteAllTextAsync(srtPath, srtContent, System.Text.Encoding.UTF8);
                logTask(task, $"[SRT] Success! Generated Whisper transcript SRT file to: {srtPath}");
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] SRT only generation failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Tries to extract a string value from a JsonElement by checking multiple property name variants.
        /// </summary>
        private string? ExtractJsonStringProperty(System.Text.Json.JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    return prop.GetString();
                }
            }
            return null;
        }

        /// <summary>
        /// Parses the SRT content from various possible API response formats.
        /// </summary>
        private string ParseSrtResponse(string responseContent)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                if (root.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return root.GetString() ?? string.Empty;
                }
                else if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    string[] possibleKeys = { "srt", "text", "transcription", "data" };
                    foreach (var key in possibleKeys)
                    {
                        if (root.TryGetProperty(key, out var prop))
                        {
                            return prop.GetString() ?? string.Empty;
                        }
                    }
                    return responseContent;
                }
            }
            catch
            {
                // If JSON parsing fails, return raw content
            }
            return responseContent;
        }
    }

    /// <summary>
    /// Minimal <see cref="IHttpClientFactory"/> used only when <see cref="VoiceoverGenerationStep"/>
    /// is constructed without a DI container (e.g. legacy hand-rolled paths or unit tests).
    /// Returns bare <see cref="HttpClient"/> instances with a 120s outer timeout and **no**
    /// retry / circuit-breaker — production callers should always go through the DI-registered
    /// named clients ("ai84", "ai84-polling") registered in <c>App.xaml.cs</c>.
    /// </summary>
    internal sealed class NoResilienceHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient
            {
                // 120s outer ceiling — replaces the 100s default that previously let one
                // stalled request block the pipeline almost indefinitely.
                Timeout = TimeSpan.FromSeconds(120)
            };
            return client;
        }
    }
}