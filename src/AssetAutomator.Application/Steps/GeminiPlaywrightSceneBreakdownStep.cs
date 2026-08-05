using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Pipeline Step 3 (Scene Creator).
    ///
    /// Two execution modes are available (set via <see cref="ExecuteAsync"/>'s <c>mode</c>):
    ///
    /// <list type="bullet">
    /// <item>
    ///   <description><b>ApiStream</b> (default): calls <c>GeminiApiService.SendChatStreamAsync</c>
    ///   against the Python REST server's <c>/api/chat/stream-extended</c> endpoint. This is
    ///   the direct C# counterpart of <c>test_gem_and_thinking.py</c> — uploads the SRT +
    ///   transcript files, picks the "Bedtime Scene Creator" gem (or any user-selected gem),
    ///   uses a thinking-capable model, and streams both <c>thoughts_delta</c> (extended
    ///   thinking process) and <c>text_delta</c> (final JSON) back to the caller in real time.
    ///   No Chrome profile required.</description>
    /// </item>
    /// <item>
    ///   <description><b>Playwright</b> (fallback): drives the actual Gemini Web UI through a
    ///   persistent Chrome profile via <see cref="GeminiPlaywrightSceneCreator"/>. Same
    ///   native-thinking benefits, but requires the user to have a Chrome profile with an
    ///   active Gemini session. Use when the Python server is unavailable or when the user
    ///   wants the exact Web UI behaviour.</description>
    /// </item>
    /// </list>
    ///
    /// IMPORTANT: When <c>mode = ApiStream</c>, the Python server <b>must</b> be reachable
    /// and the cookies.json <b>must</b> be present. The step does NOT silently fall back to
    /// Playwright on API errors — callers that want auto-fallback should detect API failures
    /// and re-invoke with <see cref="SceneBreakdownMode.Playwright"/>.
    /// </summary>
    public class GeminiPlaywrightSceneBreakdownStep
    {
        /// <summary>Minimum byte size for a usable SRT file.</summary>
        private const int MIN_SRT_BYTES = 20;

        /// <summary>
        /// Scene Creator execution mode.
        /// </summary>
        public enum SceneBreakdownMode
        {
            /// <summary>Default. Calls <see cref="GeminiApiService.SendChatStreamAsync"/> with
            /// realtime thinking/text streaming. Mirrors <c>test_gem_and_thinking.py</c>.</summary>
            ApiStream = 0,
            /// <summary>Fallback. Drives the real Gemini Web UI via Playwright + persistent Chrome profile.</summary>
            Playwright = 1,
        }

        /// <summary>
        /// Optional explicit Chrome profile path. Only used by <see cref="SceneBreakdownMode.Playwright"/>.
        /// If null, falls back to task.SelectedProfile and ConfigService.CurrentSettings.DefaultChromeProfile.
        /// </summary>
        private readonly string? _explicitProfilePath;
        private readonly IConfigService _configService;
        private readonly GeminiApiService _geminiApiService;

        public GeminiPlaywrightSceneBreakdownStep(
            IConfigService configService,
            GeminiApiService geminiApiService,
            string? chromeProfilePath = null)
        {
            _configService = configService;
            _geminiApiService = geminiApiService;
            _explicitProfilePath = chromeProfilePath;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? gemId,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? sessionId = null,
            string? gemName = null,
            SceneBreakdownMode mode = SceneBreakdownMode.ApiStream,
            bool enableExtendedThinking = true)
        {
            // sessionId is intentionally accepted but unused: Playwright keeps its own
            // browser-side conversation context. Kept in the signature for binary compatibility
            // with callers that previously passed it to the legacy step.
            _ = sessionId;

            logTask(task, $"[STEP 3] Starting Scene Breakdown (mode={mode}, gem={gemName ?? "default"}, model={selectedModel ?? "default"}, thinking={enableExtendedThinking})...");
            task.Step4Status = "Running";

            string srtPath = Path.Combine(outputDir, "voiceover.srt");
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            if (!File.Exists(srtPath))
            {
                throw new FileNotFoundException(
                    $"voiceover.srt not found at {srtPath}. " +
                    "Voiceover step must complete before Scene Creator.");
            }

            string srtContent = await File.ReadAllTextAsync(srtPath);
            if (string.IsNullOrWhiteSpace(srtContent) || srtContent.Length < MIN_SRT_BYTES)
            {
                throw new InvalidOperationException(
                    $"voiceover.srt is empty or too small ({srtContent.Length} chars).");
            }

            string resolvedModel = GeminiApiService.ResolveModelName(selectedModel);

            // ─── API STREAM MODE ───
            // Direct counterpart of test_gem_and_thinking.py: upload 2 files,
            // pick gem, ask Gemini to produce scenes JSON, stream extended
            // thinking + final text back to the caller.
            if (mode == SceneBreakdownMode.ApiStream)
            {
                await ExecuteApiStreamAsync(
                    outputDir, srtPath, transcriptPath, gemId, gemName,
                    resolvedModel, enableExtendedThinking, task, logTask);
                return;
            }

            // ─── PLAYWRIGHT MODE (fallback) ───
            // Resolve Chrome profile path with clear error messages
            string? profilePath = _explicitProfilePath ?? ResolveChromeProfilePath(task, logTask);
            if (string.IsNullOrWhiteSpace(profilePath))
            {
                throw new InvalidOperationException(
                    "[STEP 3] ❌ No Chrome profile available for Playwright. " +
                    "Configure 'DefaultChromeProfile' in Settings or select a profile for this task. " +
                    "Playwright mode does NOT fall back to the Gemini REST API by design.");
            }

            logTask(task,
                $"[STEP 3] 🎭 Playwright mode (Profile: {Path.GetFileName(profilePath)}, " +
                $"Gem: {gemName ?? "default"}, Model: {resolvedModel})...");

            var pwCreator = new GeminiPlaywrightSceneCreator(
                profilePath,
                msg => logTask(task, msg));

            bool isThinking = true; // Always attempt thinking — harmless if not available

            (string responseText, string? thoughts) = await pwCreator.CreateScenesAsync(
                outputDir: outputDir,
                srtPath: srtPath,
                transcriptPath: transcriptPath,
                gemName: gemName ?? string.Empty,
                gemId: gemId,
                modelDisplayName: resolvedModel,
                enableExtendedThinking: isThinking);

            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(outputDir, "scene_thoughts.txt"),
                    thoughts,
                    System.Text.Encoding.UTF8);
                logTask(task,
                    $"[STEP 3] 🧠 Thinking saved ({thoughts.Length} chars) → scene_thoughts.txt");
            }
            else
            {
                logTask(task, "[STEP 3] ⚠️ No thoughts captured from Web UI.");
            }

            logTask(task,
                $"[STEP 3] Response: text={responseText.Length} chars. Extracting JSON...");

            string jsonText = ExtractJsonContent(responseText);

            ScenesJsonRootModel? rootData;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);

                if (rootData == null || rootData.scenes == null || rootData.scenes.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Parsed scenes.json contains 0 scenes.");
                }
            }
            catch (Exception ex)
            {
                logTask(task,
                    $"[ERROR] [STEP 3] Failed to parse JSON from Gemini Web response: {ex.Message}");
                string rawPath = Path.Combine(outputDir, "scenes_raw_response.txt");
                await File.WriteAllTextAsync(rawPath, responseText);
                logTask(task,
                    $"[ERROR] [STEP 3] Saved raw response ({responseText.Length} chars) to: {rawPath}");
                throw;
            }

            await WriteScenesAsync(rootData, outputDir, logTask, task);
        }

        /// <summary>
        /// API Stream execution — direct counterpart of
        /// <c>test_gem_and_thinking.py</c>'s
        /// <c>client.generate_content_stream(...)</c> call. Uploads transcript.txt +
        /// voiceover.srt to the Python REST server's <c>/api/chat/stream-extended</c>
        /// endpoint, streams <c>thoughts_delta</c> (extended thinking) + <c>text_delta</c>
        /// (final JSON) back to the caller, then parses + saves <c>scenes.json</c>.
        /// </summary>
        private async Task ExecuteApiStreamAsync(
            string outputDir,
            string srtPath,
            string transcriptPath,
            string? gemId,
            string? gemName,
            string resolvedModel,
            bool enableExtendedThinking,
            AutomationTask task,
            Action<AutomationTask, string> logTask)
        {
            const string Prompt = "Tạo scenes JSON cho video từ file SRT và transcript đính kèm.";

            var attachedFiles = new List<string>();
            if (File.Exists(transcriptPath)) attachedFiles.Add(transcriptPath);
            if (File.Exists(srtPath)) attachedFiles.Add(srtPath);

            logTask(task,
                $"[STEP 3] 📡 API stream mode (Gem: {gemName ?? "default"}, Model: {resolvedModel}, " +
                $"Thinking: {enableExtendedThinking})...");
            logTask(task,
                $"[STEP 3] 📁 Attaching {attachedFiles.Count} files: " +
                $"{string.Join(", ", attachedFiles.Select(Path.GetFileName))}");
            logTask(task,
                $"[STEP 3] 🧠 Extended thinking: ON — streaming thoughts_delta realtime...");
            logTask(task, "--- [BẮT ĐẦU CHUỖI TƯ DUY / THINKING PROCESS] ---");

            var streamResult = await _geminiApiService.SendChatStreamAsync(
                message: Prompt,
                filePaths: attachedFiles,
                gemId: gemId,
                model: resolvedModel,
                enableExtendedThinking: enableExtendedThinking,
                onThoughtsDelta: delta =>
                {
                    // Surface the thinking stream to the per-step log so the
                    // user sees Gemini "reasoning" in real time — mirrors the
                    // behaviour in test_gem_and_thinking.py where the
                    // Python script prints chunk.thoughts_delta as it arrives.
                    logTask(task, $"[THINKING] {delta}");
                },
                onTextDelta: _ => { /* text is aggregated into streamResult.Text */ });

            logTask(task, "--- [KẾT THÚC CHUỖI TƯ DUY] ---");

            string responseText = streamResult.Text ?? string.Empty;
            string thoughts = streamResult.Thoughts ?? string.Empty;

            logTask(task,
                $"[STEP 3] 💡 Stream finished. text={responseText.Length} chars, " +
                $"thoughts={thoughts.Length} chars.");

            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(outputDir, "scene_thoughts.txt"),
                    thoughts,
                    System.Text.Encoding.UTF8);
                logTask(task,
                    $"[STEP 3] 🧠 Thinking saved ({thoughts.Length} chars) → scene_thoughts.txt");
            }
            else
            {
                logTask(task,
                    "[STEP 3] ⚠️ No thoughts returned — model may not support thinking.");
            }

            logTask(task,
                $"[STEP 3] Response: text={responseText.Length} chars. Extracting JSON...");

            string jsonText = ExtractJsonContent(responseText);

            ScenesJsonRootModel? rootData;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);

                if (rootData == null || rootData.scenes == null || rootData.scenes.Count == 0)
                {
                    throw new InvalidOperationException("Parsed scenes.json contains 0 scenes.");
                }
            }
            catch (Exception ex)
            {
                logTask(task,
                    $"[ERROR] [STEP 3] Failed to parse JSON from Gemini stream response: {ex.Message}");
                string rawPath = Path.Combine(outputDir, "scenes_raw_response.txt");
                await File.WriteAllTextAsync(rawPath, responseText);
                logTask(task,
                    $"[ERROR] [STEP 3] Saved raw response ({responseText.Length} chars) to: {rawPath}");
                throw;
            }

            await WriteScenesAsync(rootData, outputDir, logTask, task);
        }

        /// <summary>
        /// Serializes the parsed <see cref="ScenesJsonRootModel"/> to both
        /// <c>scenes.json</c> (canonical) and <c>output_scenes.json</c> (legacy
        /// alias consumed by downstream steps). Marks the step Done and logs the
        /// final result. Shared by API Stream and Playwright branches.
        /// </summary>
        private static async Task WriteScenesAsync(
            ScenesJsonRootModel rootData,
            string outputDir,
            Action<AutomationTask, string> logTask,
            AutomationTask task)
        {
            string formattedJson = JsonSerializer.Serialize(
                rootData,
                new JsonSerializerOptions { WriteIndented = true });

            string scenesPath = Path.Combine(outputDir, "scenes.json");
            await File.WriteAllTextAsync(scenesPath, formattedJson, System.Text.Encoding.UTF8);

            string outputScenesPath = Path.Combine(outputDir, "output_scenes.json");
            await File.WriteAllTextAsync(outputScenesPath, formattedJson, System.Text.Encoding.UTF8);

            task.Step4Status = "Done";
            logTask(task,
                $"[STEP 3] ✅ Success! Created scenes.json ({rootData.scenes.Count} scenes) at: {scenesPath}");
        }

        /// <summary>
        /// Resolves the Chrome profile path to use for Playwright.
        /// Priority: explicit constructor param > task.SelectedProfile > settings.DefaultChromeProfile.
        /// Returns null if no profile can be resolved (caller decides how to handle).
        /// </summary>
        private string? ResolveChromeProfilePath(
            AutomationTask task,
            Action<AutomationTask, string> logTask)
        {
            try
            {
                var settings = _configService.CurrentSettings;

                string profileName = !string.IsNullOrWhiteSpace(task.SelectedProfile)
                    ? task.SelectedProfile
                    : settings.DefaultChromeProfile;

                logTask(task,
                    $"[STEP 3] 🔍 Resolving Chrome profile: " +
                    $"task.SelectedProfile='{task.SelectedProfile}', " +
                    $"settings.DefaultChromeProfile='{settings.DefaultChromeProfile}'");

                if (string.IsNullOrWhiteSpace(profileName))
                {
                    logTask(task,
                        "[STEP 3] ❌ No Chrome profile configured. Set 'DefaultChromeProfile' in Settings " +
                        "or select a profile for this task.");
                    return null;
                }

                string baseDir = !string.IsNullOrWhiteSpace(settings.ChromeProfilesDir)
                    ? settings.ChromeProfilesDir
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");

                string profilePath = Path.Combine(baseDir, profileName);
                logTask(task, $"[STEP 3] 🔍 Checking profile path: {profilePath}");

                if (!Directory.Exists(profilePath))
                {
                    logTask(task, $"[STEP 3] ❌ Profile directory not found: {profilePath}");
                    return null;
                }

                logTask(task, $"[STEP 3] ✅ Profile found: {profilePath}");
                return profilePath;
            }
            catch (Exception ex)
            {
                logTask(task, $"[STEP 3] ❌ Error resolving profile: {ex.Message}");
            }
            return null;
        }

        private static string ExtractJsonContent(string text)
        {
            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            int firstBrace = text.IndexOf('{');
            int lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return text.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return text.Trim();
        }
    }
}