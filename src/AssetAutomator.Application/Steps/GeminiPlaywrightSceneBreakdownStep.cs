using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Pipeline Step 3 (Scene Creator) — Playwright-only implementation.
    ///
    /// Uses <see cref="GeminiPlaywrightSceneCreator"/> to drive the actual Gemini Web UI
    /// through a Chrome profile with an active Gemini session. This mode preserves native
    /// extended-thinking, better file handling, and avoids API 502 errors.
    ///
    /// IMPORTANT: This step intentionally does NOT fall back to the legacy
    /// Gemini REST API. If Playwright cannot run (missing Chrome profile, browser launch
    /// failure, selector timeout, etc.), the step throws and the pipeline surfaces the error.
    /// Use <see cref="GeminiSceneBreakdownStepLegacy"/> for the API fallback path.
    /// </summary>
    public class GeminiPlaywrightSceneBreakdownStep
    {
        /// <summary>Minimum byte size for a usable SRT file.</summary>
        private const int MIN_SRT_BYTES = 20;

        /// <summary>
        /// Optional explicit Chrome profile path. If null, the step falls back to
        /// task.SelectedProfile and ConfigService.CurrentSettings.DefaultChromeProfile.
        /// </summary>
        private readonly string? _explicitProfilePath;
        private readonly IConfigService _configService;

        public GeminiPlaywrightSceneBreakdownStep(IConfigService configService, string? chromeProfilePath = null)
        {
            _configService = configService;
            _explicitProfilePath = chromeProfilePath;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? gemId,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? sessionId = null,
            string? gemName = null)
        {
            // sessionId is intentionally accepted but unused: Playwright keeps its own
            // browser-side conversation context. Kept in the signature for binary compatibility
            // with callers that previously passed it to the legacy step.
            _ = sessionId;

            logTask(task, "[STEP 3] Starting Scene Breakdown via Playwright (Gemini Web UI)...");
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