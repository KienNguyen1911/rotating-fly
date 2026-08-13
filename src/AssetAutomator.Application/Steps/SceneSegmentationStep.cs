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
    /// Pipeline Stage C: Scene Segmentation.
    ///
    /// Analyzes transcript.txt and voiceover.srt to segment the video into scenes
    /// with proper timestamps. Output is scenes_raw.json (without image_prompts).
    ///
    /// This step uses a dedicated Scene Segmentation Gemini gem to focus purely on:
    /// - Understanding the narrative structure
    /// - Identifying natural scene boundaries
    /// - Extracting accurate timestamps from SRT
    /// - Assigning transcript segments to each scene
    ///
    /// Output from this step feeds into Stage D: Image Prompt Generation.
    /// </summary>
    public class SceneSegmentationStep
    {
        /// <summary>Minimum byte size for a usable SRT file.</summary>
        private const int MIN_SRT_BYTES = 20;

        /// <summary>
        /// Prompt sent to Gemini for scene segmentation.
        /// Focuses purely on understanding narrative structure and creating scene boundaries.
        /// NO image prompt generation here - that's handled in Stage D.
        /// </summary>
        internal const string SCENE_SEGMENTATION_PROMPT =
            "Phân tích file SRT và transcript để tạo danh sách scenes cho video.\n\n" +
            "NHIỀU QUY TẮC BẮT BUỘC (must follow, no exceptions):\n" +
            "1. Thời lượng mỗi scene tối đa: 20 giây. Không được vượt quá 20s.\n" +
            "2. Số từ transcript mỗi scene: tối đa ~50 từ.\n" +
            "3. Nếu một đoạn transcript dài hơn giới hạn, phải chia thành nhiều scenes " +
            "tại ranh giới tự nhiên (câu, hơi thở, ý).\n" +
            "4. Scene đóng video KHÔNG được miễn trừ. Lời chào tạm biệt/nghỉ ngơi " +
            "phải chia thành 3-6 scenes (vd: biết ơn, thở, nằm nghỉ, nói lời kết).\n" +
            "5. KHÔNG gộp nhiều SRT cues thành một scene. Mỗi scene phải có \"start\" và \"end\" " +
            "khớp với ranh giới của một hoặc nhiều SRT cues liên tiếp có tổng duration <= 20s.\n" +
            "6. Giữ nguyên timestamps từ SRT - không tự ý sửa hoặc làm tròn.\n" +
            "7. Trả về JSON thuần, bọc trong ```json ... ```, không kèm bình luận.\n" +
            "8. KHÔNG tạo image_prompt ở bước này - chỉ tạo scenes với transcript và timestamps.";

        /// <summary>
        /// Execution mode for scene segmentation.
        /// </summary>
        public enum SegmentationMode
        {
            /// <summary>Default. Uses GeminiApiService.SendChatStreamAsync with streaming.</summary>
            ApiStream = 0,
            /// <summary>Falls back to Playwright if API is unavailable.</summary>
            Playwright = 1,
        }

        private readonly IConfigService _configService;
        private readonly GeminiApiService _geminiApiService;
        private readonly string? _explicitProfilePath;

        public SceneSegmentationStep(
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
            SegmentationMode mode = SegmentationMode.ApiStream,
            bool enableExtendedThinking = true)
        {
            _ = sessionId; // Playwright manages its own conversation context

            logTask(task, $"[STAGE-C] Starting Scene Segmentation (mode={mode}, gem={gemName ?? "default"}, model={selectedModel ?? "default"}, thinking={enableExtendedThinking})...");
            task.Step3Status = "Running";

            string srtPath = Path.Combine(outputDir, "voiceover.srt");
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            // Validate input files
            if (!File.Exists(srtPath))
            {
                throw new FileNotFoundException(
                    $"voiceover.srt not found at {srtPath}. " +
                    "Voiceover step must complete before Scene Segmentation.");
            }

            string srtContent = await File.ReadAllTextAsync(srtPath);
            if (string.IsNullOrWhiteSpace(srtContent) || srtContent.Length < MIN_SRT_BYTES)
            {
                throw new InvalidOperationException(
                    $"voiceover.srt is empty or too small ({srtContent.Length} chars).");
            }

            string resolvedModel = GeminiApiService.ResolveModelName(selectedModel);

            if (mode == SegmentationMode.ApiStream)
            {
                await ExecuteApiStreamAsync(
                    outputDir, srtPath, transcriptPath, gemId, gemName,
                    resolvedModel, enableExtendedThinking, task, logTask);
                return;
            }

            // Playwright mode (fallback)
            throw new NotImplementedException(
                "[STAGE-C] Playwright mode for SceneSegmentationStep is not yet implemented. " +
                "Use ApiStream mode.");
        }

        /// <summary>
        /// API Stream execution - calls GeminiApiService.SendChatStreamAsync
        /// to segment scenes from transcript + SRT files.
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
            var attachedFiles = new List<string>();
            if (File.Exists(transcriptPath)) attachedFiles.Add(transcriptPath);
            if (File.Exists(srtPath)) attachedFiles.Add(srtPath);

            logTask(task,
                $"[STAGE-C] 📡 API stream mode (Gem: {gemName ?? "default"}, Model: {resolvedModel}, " +
                $"Thinking: {enableExtendedThinking})...");
            logTask(task,
                $"[STAGE-C] 📁 Attaching {attachedFiles.Count} files: " +
                $"{string.Join(", ", attachedFiles.Select(Path.GetFileName))}");
            logTask(task,
                $"[STAGE-C] 🧠 Extended thinking: ON — streaming thoughts_delta realtime...");
            logTask(task, "--- [BẮT ĐẦU CHUỖI TƯ DUY / THINKING PROCESS] ---");

            var streamResult = await _geminiApiService.SendChatStreamAsync(
                message: SCENE_SEGMENTATION_PROMPT,
                filePaths: attachedFiles,
                gemId: gemId,
                model: resolvedModel,
                enableExtendedThinking: enableExtendedThinking,
                onThoughtsDelta: delta =>
                {
                    logTask(task, $"[THINKING] {delta}");
                },
                onTextDelta: _ => { /* text is aggregated into streamResult.Text */ });

            logTask(task, "--- [KẾT THÚC CHUỖI TƯ DUY] ---");

            string responseText = streamResult.Text ?? string.Empty;
            string thoughts = streamResult.Thoughts ?? string.Empty;

            logTask(task,
                $"[STAGE-C] 💡 Stream finished. text={responseText.Length} chars, " +
                $"thoughts={thoughts.Length} chars.");

            if (!string.IsNullOrWhiteSpace(thoughts))
            {
                await File.WriteAllTextAsync(
                    Path.Combine(outputDir, "segmentation_thoughts.txt"),
                    thoughts,
                    System.Text.Encoding.UTF8);
                logTask(task,
                    $"[STAGE-C] 🧠 Thinking saved ({thoughts.Length} chars) → segmentation_thoughts.txt");
            }
            else
            {
                logTask(task, "[STAGE-C] ⚠️ No thoughts returned — model may not support thinking.");
            }

            logTask(task,
                $"[STAGE-C] Response: text={responseText.Length} chars. Extracting JSON...");

            string jsonText = ExtractJsonContent(responseText);

            SceneSegmentationModel? rootData;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                rootData = JsonSerializer.Deserialize<SceneSegmentationModel>(jsonText, options);

                if (rootData == null || rootData.scenes == null || rootData.scenes.Count == 0)
                {
                    throw new InvalidOperationException("Parsed scenes_raw.json contains 0 scenes.");
                }
            }
            catch (Exception ex)
            {
                logTask(task,
                    $"[ERROR] [STAGE-C] Failed to parse JSON from Gemini stream response: {ex.Message}");
                string rawPath = Path.Combine(outputDir, "segmentation_raw_response.txt");
                await File.WriteAllTextAsync(rawPath, responseText);
                logTask(task,
                    $"[ERROR] [STAGE-C] Saved raw response ({responseText.Length} chars) to: {rawPath}");
                throw;
            }

            await WriteScenesRawAsync(rootData, outputDir, logTask, task);
        }

        /// <summary>
        /// Writes the parsed SceneSegmentationModel to scenes_raw.json.
        /// </summary>
        private async Task WriteScenesRawAsync(
            SceneSegmentationModel rootData,
            string outputDir,
            Action<AutomationTask, string> logTask,
            AutomationTask task)
        {
            string formattedJson = JsonSerializer.Serialize(
                rootData,
                new JsonSerializerOptions { WriteIndented = true });

            string scenesRawPath = Path.Combine(outputDir, "scenes_raw.json");
            await File.WriteAllTextAsync(scenesRawPath, formattedJson, System.Text.Encoding.UTF8);

            task.Step3Status = "Done";
            logTask(task,
                $"[STAGE-C] ✅ Success! Created scenes_raw.json ({rootData.scenes.Count} scenes) at: {scenesRawPath}");
        }

        private static string ExtractJsonContent(string text)
        {
            // First, try to find a ```json``` code block specifically (preferred)
            var jsonBlockMatch = Regex.Match(text, @"```json\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (jsonBlockMatch.Success)
            {
                return jsonBlockMatch.Groups[1].Value.Trim();
            }

            // Fallback: find ANY code block (```...```) and extract content
            // This handles cases where the model wraps output in code fences without language tag
            var anyBlockMatch = Regex.Match(text, @"```\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (anyBlockMatch.Success)
            {
                string extracted = anyBlockMatch.Groups[1].Value.Trim();
                // If the extracted content looks like JSON (starts with { or [), return it
                if (extracted.StartsWith("{") || extracted.StartsWith("["))
                {
                    return extracted;
                }
            }

            // Last resort: find first '{' and last '}'
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
