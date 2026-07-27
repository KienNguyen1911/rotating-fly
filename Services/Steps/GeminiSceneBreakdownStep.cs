using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetAutomator.Services;

namespace AssetAutomator
{
    /// <summary>
    /// Pipeline Step 4: Takes voiceover.srt + transcript.txt, sends them to Gemini Gem "Scene Creator",
    /// and parses the response into scenes.json containing timestamps, narration, and visual image prompts.
    /// </summary>
    public class GeminiSceneBreakdownStep
    {
        private readonly GeminiApiService _geminiApiService;

        public GeminiSceneBreakdownStep(GeminiApiService geminiApiService)
        {
            _geminiApiService = geminiApiService;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? gemId,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? selectedExtension = null,
            string? sessionId = null)
        {
            logTask(task, "[STEP 4] Starting Scene Breakdown & Prompt Generation via Gemini Scene Creator Gem...");
            task.Step4Status = "In Progress";

            string srtPath = Path.Combine(outputDir, "voiceover.srt");
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            if (!File.Exists(srtPath))
            {
                throw new FileNotFoundException($"voiceover.srt not found at {srtPath}. Please complete Step 3 (Voiceover/SRT) first.");
            }

            // Validate SRT file has actual content
            string srtContent = await File.ReadAllTextAsync(srtPath);
            if (string.IsNullOrWhiteSpace(srtContent) || srtContent.Length < 20)
            {
                throw new InvalidOperationException($"voiceover.srt exists but appears empty or invalid ({srtContent.Length} chars). Cannot create scene breakdown.");
            }

            // Build a detailed prompt with exact JSON format specification
            string prompt = @"Bạn là một Scene Creator chuyên nghiệp. Hãy đọc nội dung từ file transcript.txt và voiceover.srt đính kèm, sau đó chuyển đổi chúng thành 1 đối tượng JSON phân cảnh duy nhất.

Cấu trúc JSON bắt buộc tuân thủ 100% (chỉ trả về JSON thuần trong khối ```json ```, không kèm câu dẫn, không kèm code python, không kèm giải thích):

```json
{
    ""video_title"": ""Tên video tự động sinh từ nội dung"",
    ""scene_count"": 5,
    ""scenes"": [
        {
            ""scene"": 1,
            ""id"": ""scene_001"",
            ""time"": {
                ""start"": ""00:00:00,000"",
                ""end"": ""00:00:05,500"",
                ""duration"": 5.5
            },
            ""transcript"": ""Nội dung câu nói ở cảnh này (lấy từ transcript.txt)"",
            ""image_prompt"": ""Mô tả chi tiết bằng tiếng Anh những gì diễn ra trong cảnh, bao gồm góc quay, ánh sáng, đối tượng, chất liệu, tâm trạng. Phong cách: Minimalist golden neon line art stickman doodle style, dark 2D lo-fi aesthetic, pitch black background.""
        }
    ]
}
```

Yêu cầu QUAN TRỌNG:
1. Mỗi scene có duration 3-6 giây, dựa trên timing từ file voiceover.srt
2. image_prompt viết bằng TIẾNG ANH, mô tả trực quan phù hợp với cảm xúc đoạn transcript
3. Phong cách image_prompt: Minimalist golden neon line art stickman doodle style, dark 2D lo-fi aesthetic, pitch black background
4. KHÔNG kèm câu dẫn, không kèm giải thích, không kèm code python - CHỈ trả về JSON thuần
5. Số lượng scene phải khớp với nội dung transcript và timing từ SRT";

            var attachedFiles = new List<string>();
            if (File.Exists(transcriptPath)) attachedFiles.Add(transcriptPath);
            if (File.Exists(srtPath)) attachedFiles.Add(srtPath);

            logTask(task, $"[STEP 4] Attaching {attachedFiles.Count} files ({string.Join(", ", attachedFiles.Select(Path.GetFileName))}) with detailed JSON format prompt to Gemini Gem (Gem ID: {gemId ?? "Scene Creator"}, Model: {selectedModel ?? "Default"})...");

            var response = await _geminiApiService.SendChatAsync(
                message: prompt,
                gemId: gemId,
                model: selectedModel,
                extension: selectedExtension,
                sessionId: sessionId,
                filePaths: attachedFiles
            );

            if (string.IsNullOrWhiteSpace(response.text))
            {
                throw new InvalidOperationException("[STEP 4] Gemini API returned empty response for scene breakdown.");
            }

            // Log raw response length for debugging
            logTask(task, $"[STEP 4] Received Gemini response ({response.text.Length} chars). Extracting JSON...");

            // Clean and extract JSON string
            string jsonText = ExtractJsonContent(response.text);

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
                await File.WriteAllTextAsync(rawPath, response.text);
                logTask(task, $"[ERROR] [STEP 4] Saved raw response ({response.text.Length} chars) to: {rawPath}");
                throw;
            }

            string formattedJson = JsonSerializer.Serialize(rootData, new JsonSerializerOptions { WriteIndented = true });
            
            string scenesPath = Path.Combine(outputDir, "scenes.json");
            await File.WriteAllTextAsync(scenesPath, formattedJson, System.Text.Encoding.UTF8);

            string outputScenesPath = Path.Combine(outputDir, "output_scenes.json");
            await File.WriteAllTextAsync(outputScenesPath, formattedJson, System.Text.Encoding.UTF8);

            task.Step4Status = "Completed";
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
