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
    /// LEGACY — Scene Breakdown via Gemini REST API.
    ///
    /// Kept as an opt-in fallback when Playwright is not viable (no Chrome profile,
    /// headless CI environments, or when the user explicitly disables browser automation).
    /// The active production pipeline uses <see cref="GeminiPlaywrightSceneBreakdownStep"/>,
    /// which drives the real Gemini Web UI through a persistent Chrome profile.
    ///
    /// This class intentionally preserves the original Playwright → API fallback chain so
    /// callers that still hold a reference (legacy code paths, debugging) keep working.
    /// New callers should depend on <see cref="GeminiPlaywrightSceneBreakdownStep"/>.
    /// </summary>
    [Obsolete("Use GeminiPlaywrightSceneBreakdownStep instead. This Legacy class is only kept as an opt-in API-mode fallback.")]
    public class GeminiSceneBreakdownStepLegacy
    {
        private readonly GeminiApiService _geminiApiService;
        private readonly IConfigService _configService;
        private readonly string? _explicitProfilePath;

        public GeminiSceneBreakdownStepLegacy(GeminiApiService geminiApiService, IConfigService configService, string? chromeProfilePath = null)
        {
            _geminiApiService = geminiApiService;
            _configService = configService;
            _explicitProfilePath = chromeProfilePath;
        }

        /// <summary>
        /// Resolves the Chrome profile path to use for Playwright.
        /// Priority: task.SelectedProfile > settings.DefaultChromeProfile.
        /// </summary>
        private string? ResolveChromeProfilePath(AutomationTask task, Action<AutomationTask, string> logTask)
        {
            try
            {
                var settings = _configService.CurrentSettings;

                // Determine profile name: task selection first, then settings default
                string profileName = !string.IsNullOrWhiteSpace(task.SelectedProfile)
                    ? task.SelectedProfile
                    : settings.DefaultChromeProfile;

                logTask(task, $"[STEP 4] 🔍 Resolving Chrome profile: task.SelectedProfile='{task.SelectedProfile}', settings.DefaultChromeProfile='{settings.DefaultChromeProfile}'");

                if (string.IsNullOrWhiteSpace(profileName))
                {
                    logTask(task, "[STEP 4] ❌ No Chrome profile configured. Set 'DefaultChromeProfile' in Settings or select a profile for this task.");
                    return null;
                }

                // Determine base directory
                string baseDir = !string.IsNullOrWhiteSpace(settings.ChromeProfilesDir)
                    ? settings.ChromeProfilesDir
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");

                string profilePath = Path.Combine(baseDir, profileName);
                logTask(task, $"[STEP 4] 🔍 Checking profile path: {profilePath}");

                if (!Directory.Exists(profilePath))
                {
                    logTask(task, $"[STEP 4] ❌ Profile directory not found: {profilePath}");
                    return null;
                }

                logTask(task, $"[STEP 4] ✅ Profile found: {profilePath}");
                return profilePath;
            }
            catch (Exception ex)
            {
                logTask(task, $"[STEP 4] ❌ Error resolving profile: {ex.Message}");
            }
            return null;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? gemId,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? sessionId = null,
            string? gemName = null,
            bool usePlaywright = true)
        {
            logTask(task, "[STEP 4] Starting Scene Breakdown & Prompt Generation via Gemini Scene Creator Gem...");
            task.Step4Status = "Running";

            string srtPath = Path.Combine(outputDir, "voiceover.srt");
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            if (!File.Exists(srtPath))
                throw new FileNotFoundException($"voiceover.srt not found at {srtPath}.");

            string srtContent = await File.ReadAllTextAsync(srtPath);
            if (string.IsNullOrWhiteSpace(srtContent) || srtContent.Length < 20)
                throw new InvalidOperationException($"voiceover.srt empty ({srtContent.Length} chars).");

            string resolvedModel = GeminiApiService.ResolveModelName(selectedModel);
            string responseText = "";

            // Same hardened prompt used by the production step so the legacy
            // API fallback path stays in lockstep on the "no 60-100s scenes"
            // rule. SceneDurationFixer further enforces the rule below.
            const string SCENE_CREATION_PROMPT =
                "Tạo scenes JSON cho video từ file SRT và transcript đính kèm.\n\n" +
                "HARD RULES (must follow, no exceptions, including the final scene):\n" +
                "1. Maximum duration per scene: 20 seconds. Never exceed 20s.\n" +
                "2. Maximum transcript words per scene: ~50 words.\n" +
                "3. If a transcript segment is longer than the limit, you MUST split it " +
                "into multiple scenes at natural sentence or breath boundaries. Each " +
                "scene must be self-contained with its own image_prompt.\n" +
                "4. The closing scene is NOT exempt. A long farewell/breath/sleep-well " +
                "narration must be split into 3-6 scenes (e.g. gratitude, breath, " +
                "settling-in, deep rest, closing words).\n" +
                "5. Do NOT merge multiple SRT cues into one scene. Each scene's " +
                "\"start\" and \"end\" must match the boundaries of one or more " +
                "consecutive SRT cues whose total duration is <= 20s.\n" +
                "6. Return SRT timestamps verbatim — do not invent or round them.\n" +
                "7. Output JSON only, wrapped in ```json ... ```, no commentary.";

            // Resolve profile path: explicit constructor param > task.SelectedProfile > settings.DefaultChromeProfile
            string? profilePath = _explicitProfilePath ?? ResolveChromeProfilePath(task, logTask);

            // ── Playwright mode ──
            if (usePlaywright && !string.IsNullOrWhiteSpace(profilePath))
            {
                try
                {
                    logTask(task, $"[STEP 4] 🎭 Playwright browser mode (Profile: {Path.GetFileName(profilePath)}, Gem: {gemName ?? "default"}, Model: {resolvedModel})...");
                    var pwCreator = new GeminiPlaywrightSceneCreator(profilePath, msg => logTask(task, msg));
                    bool isThinking = true; // Always attempt thinking — harmless if not available

                    string? thoughts;
                    (responseText, thoughts) = await pwCreator.CreateScenesAsync(
                        outputDir, srtPath, transcriptPath, gemName ?? "", gemId, resolvedModel, isThinking);

                    if (!string.IsNullOrWhiteSpace(thoughts))
                    {
                        await File.WriteAllTextAsync(Path.Combine(outputDir, "scene_thoughts.txt"), thoughts, System.Text.Encoding.UTF8);
                        logTask(task, $"[STEP 4] 🧠 Thinking saved ({thoughts.Length} chars) → scene_thoughts.txt");
                    }
                }
                catch (Exception pwEx)
                {
                    logTask(task, $"[STEP 4] ⚠️ Playwright failed: {pwEx.Message}. Falling back to API...");
                    usePlaywright = false;
                }
            }
            else if (usePlaywright)
            {
                logTask(task, "[STEP 4] ⚠️ Playwright mode enabled but no Chrome profile available. Check logs above for details. Falling back to API...");
                usePlaywright = false;
            }

            // ── API mode ──
            if (!usePlaywright)
            {
                var attachedFiles = new List<string>();
                if (File.Exists(transcriptPath)) attachedFiles.Add(transcriptPath);
                if (File.Exists(srtPath)) attachedFiles.Add(srtPath);

                string prompt = SCENE_CREATION_PROMPT;
                logTask(task, $"[STEP 4] API mode: {attachedFiles.Count} files, Gem={gemId ?? "default"}, Model={resolvedModel}");

                var response = await _geminiApiService.SendChatAsync(
                    message: prompt, gemId: gemId, model: resolvedModel,
                    sessionId: sessionId, filePaths: attachedFiles);

                if (string.IsNullOrWhiteSpace(response.text))
                    throw new InvalidOperationException("[STEP 4] Gemini API returned empty response.");

                responseText = response.text;

                if (!string.IsNullOrWhiteSpace(response.thoughts))
                {
                    await File.WriteAllTextAsync(Path.Combine(outputDir, "scene_thoughts.txt"), response.thoughts, System.Text.Encoding.UTF8);
                    logTask(task, $"[STEP 4] 🧠 Thinking saved ({response.thoughts.Length} chars) → scene_thoughts.txt");
                }
                else
                    logTask(task, "[STEP 4] ⚠️ No thoughts returned — model may not support thinking.");
            }

            logTask(task, $"[STEP 4] Response: text={responseText.Length} chars. Extracting JSON...");
            string jsonText = ExtractJsonContent(responseText);

            // Validate JSON parsing
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
                logTask(task, $"[ERROR] [STEP 4] Failed to parse JSON from Gemini response: {ex.Message}");
                // Fallback: save raw output for debugging
                string rawPath = Path.Combine(outputDir, "scenes_raw_response.txt");
                await File.WriteAllTextAsync(rawPath, responseText);
                logTask(task, $"[ERROR] [STEP 4] Saved raw response ({responseText.Length} chars) to: {rawPath}");
                throw;
            }

            try
            {
                // SceneDurationFixer has been removed.
                // Gemini's scene breakdown now relies on the model itself for proper segmentation.
                // If scenes are too long, callers should regenerate with adjusted parameters.
                logTask(task, "[STEP 4] 🛡️ Skipped post-processing; using Gemini's scene segmentation as-is.");
            }
            catch (Exception fixEx)
            {
                logTask(task,
                    $"[STEP 4] ⚠️ Processing skipped: {fixEx.Message}. " +
                    "Continuing with raw Gemini output.");
            }

            string formattedJson = JsonSerializer.Serialize(rootData, new JsonSerializerOptions { WriteIndented = true });

            string scenesPath = Path.Combine(outputDir, "scenes.json");
            await File.WriteAllTextAsync(scenesPath, formattedJson, System.Text.Encoding.UTF8);

            string outputScenesPath = Path.Combine(outputDir, "output_scenes.json");
            await File.WriteAllTextAsync(outputScenesPath, formattedJson, System.Text.Encoding.UTF8);

            task.Step4Status = "Done";
            logTask(task, $"[STEP 4] Success! Successfully created scenes.json ({rootData.scenes.Count} scenes) at: {scenesPath}");
        }

        private string ExtractJsonContent(string text)
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