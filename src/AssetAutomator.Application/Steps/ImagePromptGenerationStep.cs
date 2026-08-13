using System;
using System.Collections.Generic;
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
    /// Pipeline Stage D: Image Prompt Generation.
    ///
    /// Reads scenes_raw.json (from Stage C) and generates image_prompts for each scene.
    /// This step uses a dedicated Image Prompt Generation Gemini gem to focus purely on:
    /// - Creating vivid, detailed image prompts
    /// - Matching the visual style (sleep/bedtime theme)
    /// - Ensuring consistency with character references
    /// - Describing appropriate imagery for each scene's narrative
    ///
    /// Input: scenes_raw.json, transcript.txt (optional for context)
    /// Output: scenes.json (full scenes with image_prompts)
    /// </summary>
    public class ImagePromptGenerationStep
    {
        private readonly IConfigService _configService;
        private readonly GeminiApiService _geminiApiService;
        private readonly string? _explicitProfilePath;

        public ImagePromptGenerationStep(
            IConfigService configService,
            GeminiApiService geminiApiService,
            string? chromeProfilePath = null)
        {
            _configService = configService;
            _geminiApiService = geminiApiService;
            _explicitProfilePath = chromeProfilePath;
        }

        /// <summary>
        /// Execution mode for image prompt generation.
        /// </summary>
        public enum ImagePromptMode
        {
            /// <summary>Default. Uses GeminiApiService.SendChatStreamAsync with streaming.</summary>
            ApiStream = 0,
            /// <summary>Falls back to Playwright if API is unavailable.</summary>
            Playwright = 1,
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? gemId,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? sessionId = null,
            string? gemName = null,
            ImagePromptMode mode = ImagePromptMode.ApiStream,
            bool enableExtendedThinking = true)
        {
            _ = sessionId;

            logTask(task, $"[STAGE-D] Starting Image Prompt Generation (mode={mode}, gem={gemName ?? "default"}, model={selectedModel ?? "default"}, thinking={enableExtendedThinking})...");
            task.Step4Status = "Running";

            string scenesRawPath = Path.Combine(outputDir, "scenes_raw.json");

            // Validate input
            if (!File.Exists(scenesRawPath))
            {
                throw new FileNotFoundException(
                    $"scenes_raw.json not found at {scenesRawPath}. " +
                    "Scene Segmentation step must complete before Image Prompt Generation.");
            }

            string scenesRawContent = await File.ReadAllTextAsync(scenesRawPath);
            if (string.IsNullOrWhiteSpace(scenesRawContent))
            {
                throw new InvalidOperationException(
                    $"scenes_raw.json is empty at {scenesRawPath}.");
            }

            string resolvedModel = GeminiApiService.ResolveModelName(selectedModel);

            if (mode == ImagePromptMode.ApiStream)
            {
                await ExecuteApiStreamAsync(
                    outputDir, scenesRawPath, gemId, gemName,
                    resolvedModel, enableExtendedThinking, task, logTask);
                return;
            }

            // Playwright mode (fallback)
            throw new NotImplementedException(
                "[STAGE-D] Playwright mode for ImagePromptGenerationStep is not yet implemented. " +
                "Use ApiStream mode.");
        }

    /// <summary>
    /// API Stream execution - generates image prompts for scenes.
    /// </summary>
    private async Task ExecuteApiStreamAsync(
        string outputDir,
        string scenesRawPath,
        string? gemId,
        string? gemName,
        string resolvedModel,
        bool enableExtendedThinking,
        AutomationTask task,
        Action<AutomationTask, string> logTask)
    {
        // Read scenes_raw.json
        string scenesRawContent = await File.ReadAllTextAsync(scenesRawPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var rawModel = JsonSerializer.Deserialize<SceneSegmentationModel>(scenesRawContent, options);

        if (rawModel == null || rawModel.scenes == null || rawModel.scenes.Count == 0)
        {
            throw new InvalidOperationException("scenes_raw.json contains no scenes.");
        }

        string videoTitle = rawModel.video_title;
        int sceneCount = rawModel.scenes.Count;

        logTask(task,
            $"[STAGE-D] 📡 API stream mode (Gem: {gemName ?? "default"}, Model: {resolvedModel}, " +
            $"Thinking: {enableExtendedThinking})...");
        logTask(task,
            $"[STAGE-D] 📁 Processing {sceneCount} scenes from scenes_raw.json...");
        logTask(task,
            $"[STAGE-D] 🧠 Extended thinking: ON — streaming thoughts_delta realtime...");
        logTask(task, "--- [BẮT ĐẦU CHUỖI TƯ DUY / THINKING PROCESS] ---");

        // Build prompt for image prompt generation
        string prompt = BuildImagePromptRequest(rawModel);

        // scenes_raw.json is the only file needed - it contains all scene transcripts
        var attachedFiles = new List<string> { scenesRawPath };

        var streamResult = await _geminiApiService.SendChatStreamAsync(
                message: prompt,
                filePaths: attachedFiles,
                gemId: gemId,
                model: resolvedModel,
                enableExtendedThinking: enableExtendedThinking,
                onThoughtsDelta: delta =>
                {
                    logTask(task, $"[THINKING] {delta}");
                },
                onTextDelta: _ => { });

            logTask(task, "--- [KẾT THÚC CHUỖI TƯ DUY] ---");

            string responseText = streamResult.Text ?? string.Empty;
            string thoughts = streamResult.Thoughts ?? string.Empty;

            logTask(task,
                $"[STAGE-D] 💡 Stream finished. text={responseText.Length} chars, " +
                $"thoughts={thoughts.Length} chars.");

            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(outputDir, "imageprompt_thoughts.txt"),
                    thoughts,
                    System.Text.Encoding.UTF8);
                logTask(task,
                    $"[STAGE-D] 🧠 Thinking saved ({thoughts.Length} chars) → imageprompt_thoughts.txt");
            }
            else
            {
                logTask(task, "[STAGE-D] ⚠️ No thoughts returned — model may not support thinking.");
            }

            logTask(task,
                $"[STAGE-D] Response: text={responseText.Length} chars. Extracting JSON...");

            string jsonText = ExtractJsonContent(responseText);

            try
            {
                // v4.0 DRY format: Gemini returns {"image_prompt_postfix": "...", "scenes": {"001": "...", "002": "..."}}
                // We need to parse this, extract postfix, and merge content + postfix into scenes
                var optionsJson = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var imagePromptResponse = JsonSerializer.Deserialize<ImagePromptResponseModel>(jsonText, optionsJson);

                if (imagePromptResponse == null)
                {
                    throw new InvalidOperationException("Image prompt response is null.");
                }

                string postfix = imagePromptResponse.image_prompt_postfix ?? string.Empty;
                var imagePromptsDict = imagePromptResponse.scenes;

                if (imagePromptsDict == null || imagePromptsDict.Count == 0)
                {
                    throw new InvalidOperationException("Image prompt response contains 0 scene entries.");
                }

                if (!string.IsNullOrWhiteSpace(postfix))
                {
                    logTask(task, $"[STAGE-D] 📦 Parsed v4.0 DRY format: {imagePromptsDict.Count} scenes + postfix ({postfix.Length} chars)");
                }
                else
                {
                    logTask(task, $"[STAGE-D] ⚠️ No image_prompt_postfix found (v4.0 format). Falling back to simple format.");
                    // Fallback: treat entire response as simple dict
                    var simpleDict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonText, optionsJson);
                    if (simpleDict != null)
                    {
                        imagePromptsDict = simpleDict;
                        postfix = string.Empty;
                    }
                }

                // Merge image prompts (with postfix) into the raw scenes
                await MergeImagePromptsAsync(rawModel, imagePromptsDict, postfix, outputDir, logTask, task);
            }
            catch (Exception ex)
            {
                logTask(task,
                    $"[ERROR] [STAGE-D] Failed to parse JSON from Gemini stream response: {ex.Message}");
                string rawPath = Path.Combine(outputDir, "imageprompt_raw_response.txt");
                await File.WriteAllTextAsync(rawPath, responseText);
                logTask(task,
                    $"[ERROR] [STAGE-D] Saved raw response ({responseText.Length} chars) to: {rawPath}");
                throw;
            }
        }

        /// <summary>
        /// Merges image prompts (keyed by scene number) into scenes_raw.json and writes final scenes.json.
        /// v4.0 DRY: If postfix is provided, it will be appended to each scene's image_prompt.
        /// </summary>
        private async Task MergeImagePromptsAsync(
            SceneSegmentationModel rawModel,
            Dictionary<string, string> imagePrompts,
            string postfix,
            string outputDir,
            Action<AutomationTask, string> logTask,
            AutomationTask task)
        {
            // Build the final scenes list
            var finalScenes = new List<SceneJsonEntryModel>();

            foreach (var segment in rawModel.scenes)
            {
                var entry = new SceneJsonEntryModel
                {
                    scene = segment.scene,
                    id = segment.id,
                    time = segment.time,
                    transcript = segment.transcript,
                    image_prompt = string.Empty // Default empty
                };

                // Find matching image prompt by scene number (001, 002, etc.)
                string key = segment.scene.ToString("D3");
                if (imagePrompts.TryGetValue(key, out var prompt) && !string.IsNullOrWhiteSpace(prompt))
                {
                    // v4.0 DRY: Append postfix if available
                    string finalPrompt = prompt.Trim();
                    if (!string.IsNullOrWhiteSpace(postfix))
                    {
                        finalPrompt = $"{finalPrompt}, {postfix}";
                    }
                    entry.image_prompt = finalPrompt;
                    logTask(task, $"[STAGE-D] ✅ Scene {key}: assigned image_prompt ({prompt.Length} chars + postfix {postfix.Length} chars)");
                }
                else
                {
                    logTask(task, $"[STAGE-D] ⚠️ Scene {key}: no image_prompt found, leaving empty");
                }

                finalScenes.Add(entry);
            }

            // Build final root model with postfix for reference
            var rootData = new ScenesJsonRootModel
            {
                video_title = rawModel.video_title,
                scene_count = finalScenes.Count,
                image_prompt_postfix = string.IsNullOrWhiteSpace(postfix) ? null : postfix,
                scenes = finalScenes
            };

            await WriteScenesAsync(rootData, outputDir, logTask, task);
        }

        /// <summary>
        /// Builds the prompt for Gemini to generate image prompts for each scene.
        /// Note: The Gemini Gem has its own system instructions (image-prompt-creator.md).
        /// scenes_raw.json is already attached as a file, so we only need minimal instruction.
        /// v4.0: Instructs Gemini to use DRY format with separate postfix.
        /// </summary>
        private string BuildImagePromptRequest(SceneSegmentationModel rawModel)
        {
            return $"Generate image prompts for video: {rawModel.video_title}\n\n" +
                   "Read the attached scenes_raw.json file and generate one image prompt per scene.\n\n" +
                   "IMPORTANT: Use v4.0 DRY format with SEPARATE postfix:\n" +
                   "{\n" +
                   "  \"image_prompt_postfix\": \"style signature + negative prompt + aspect ratio (COMMON for all scenes)\",\n" +
                   "  \"scenes\": {\n" +
                   "    \"001\": \"scene content WITHOUT postfix (background + mascot + action + props)\",\n" +
                   "    \"002\": \"scene content WITHOUT postfix...\",\n" +
                   "    ...\n" +
                   "  }\n" +
                   "}\n\n" +
                   "The final prompt will be constructed as: scene_content + \", \" + image_prompt_postfix\n\n" +
                   "Return JSON ONLY wrapped in ```json ... ```, no commentary.";
        }

        /// <summary>
        /// Writes the final scenes.json with image_prompts.
        /// </summary>
        private async Task WriteScenesAsync(
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

            // Also write output_scenes.json for legacy compatibility
            string outputScenesPath = Path.Combine(outputDir, "output_scenes.json");
            await File.WriteAllTextAsync(outputScenesPath, formattedJson, System.Text.Encoding.UTF8);

            task.Step4Status = "Done";
            logTask(task,
                $"[STAGE-D] ✅ Success! Created scenes.json ({rootData.scenes.Count} scenes with image_prompts) at: {scenesPath}");
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
