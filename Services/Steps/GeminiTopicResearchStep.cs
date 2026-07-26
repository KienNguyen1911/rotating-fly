using System;
using System.IO;
using System.Threading.Tasks;
using AssetAutomator.Services;

namespace AssetAutomator
{
    /// <summary>
    /// Pipeline Step 2: Researches a video topic using Gemini WebAPI (with Deep Research + Gem selection)
    /// and generates a complete video transcript saved as transcript.txt.
    /// </summary>
    public class GeminiTopicResearchStep
    {
        private readonly GeminiApiService _geminiApiService;

        public GeminiTopicResearchStep(GeminiApiService geminiApiService)
        {
            _geminiApiService = geminiApiService;
        }

        public async Task ExecuteAsync(
            string topicOrUrl,
            string outputDir,
            string? gemId,
            bool enableDeepResearch,
            AutomationTask task,
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 2] Starting Deep Research & Transcript Generation via Gemini API...");
            task.Step2Status = "In Progress";

            if (string.IsNullOrWhiteSpace(topicOrUrl))
            {
                throw new ArgumentException("Topic or Video URL cannot be empty for Topic Research Step.");
            }

            Directory.CreateDirectory(outputDir);

            string prompt = $"Hãy nghiên cứu chuyên sâu về chủ đề/nội dung sau đây và viết một bản kịch bản (transcript) video chi tiết, hoàn chỉnh, lôi cuốn bằng tiếng Việt:\n\nChủ đề: {topicOrUrl}";

            logTask(task, $"[STEP 2] Sending request to Gemini API (Gem ID: {gemId ?? "Default"}, Deep Research: {enableDeepResearch})...");

            var response = await _geminiApiService.SendChatAsync(
                message: prompt,
                gemId: gemId,
                deepResearch: enableDeepResearch
            );

            if (string.IsNullOrWhiteSpace(response.text))
            {
                throw new InvalidOperationException("[STEP 2] Gemini API returned empty transcript.");
            }

            string transcriptPath = Path.Combine(outputDir, "transcript.txt");
            await File.WriteAllTextAsync(transcriptPath, response.text, System.Text.Encoding.UTF8);

            task.Step2Status = "Completed";
            logTask(task, $"[STEP 2] Success! Saved generated transcript to: {transcriptPath}");
        }
    }
}
