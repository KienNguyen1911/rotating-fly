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
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 4] Starting Scene Breakdown & Prompt Generation via Gemini Scene Creator Gem...");
            task.Step4Status = "In Progress";

            string srtPath = Path.Combine(outputDir, "voiceover.srt");
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            if (!File.Exists(srtPath))
            {
                throw new FileNotFoundException($"voiceover.srt not found at {srtPath}. Please complete Step 3 (Voiceover/SRT) first.");
            }

            string srtContent = await File.ReadAllTextAsync(srtPath);
            string transcriptContent = File.Exists(transcriptPath) ? await File.ReadAllTextAsync(transcriptPath) : string.Empty;

            string prompt = $@"Dựa trên kịch bản (transcript) và file phụ đề SRT dưới đây, hãy đóng vai là một Video Director chuyên nghiệp để phân chia video thành từng Scene chi tiết.

### YÊU CẦU ĐẦU RA:
Bắt buộc CHỈ TRẢ VỀ DUY NHẤT một chuỗi JSON hợp lệ (không kèm lời mở đầu hay giải thích) theo cấu trúc mẫu sau:

```json
{{
    ""video_title"": ""Tên video"",
    ""scene_count"": 2,
    ""scenes"": [
        {{
            ""scene"": 1,
            ""id"": ""scene_001"",
            ""time"": {{
                ""start"": ""00:00:00,099"",
                ""end"": ""00:00:11,679"",
                ""duration"": 11.58
            }},
            ""transcript"": ""Lời thoại của phân cảnh này..."",
            ""image_prompt"": ""Close-up of character... minimalist 2D style...""
        }}
    ]
}}
```

### NỘI DUNG SRT:
{srtContent}

{(string.IsNullOrWhiteSpace(transcriptContent) ? "" : $"### NỘI DUNG TRANSCRIPT THÔ:\n{transcriptContent}")}";

            logTask(task, $"[STEP 4] Sending prompt to Gemini Gem (Gem ID: {gemId ?? "Scene Creator"})...");
            var response = await _geminiApiService.SendChatAsync(prompt, gemId: gemId);

            if (string.IsNullOrWhiteSpace(response.text))
            {
                throw new InvalidOperationException("[STEP 4] Gemini API returned empty response for scene breakdown.");
            }

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
                await File.WriteAllTextAsync(Path.Combine(outputDir, "scenes_raw_response.txt"), response.text);
                throw;
            }

            string scenesPath = Path.Combine(outputDir, "scenes.json");
            string formattedJson = JsonSerializer.Serialize(rootData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(scenesPath, formattedJson, System.Text.Encoding.UTF8);

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
