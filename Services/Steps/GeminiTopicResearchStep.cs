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

        public async Task<string?> ExecuteAsync(
            string topicOrUrl,
            string outputDir,
            string? gemId,
            bool enableDeepResearch,
            AutomationTask task,
            Action<AutomationTask, string> logTask,
            string? selectedModel = null,
            string? existingSessionId = null)
        {
            logTask(task, "[STEP 2] Starting Deep Research & Transcript Generation via Gemini API...");

            Directory.CreateDirectory(outputDir);
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            // ── Defensive skip: if transcript already exists with content, return existing session ──
            if (File.Exists(transcriptPath) && new FileInfo(transcriptPath).Length > 50)
            {
                logTask(task, $"[STEP 2] ⏭️ transcript.txt đã tồn tại ({new FileInfo(transcriptPath).Length} bytes), bỏ qua Deep Research.");
                task.Step2Status = "Done";
                return existingSessionId; // caller decides what to do with null session
            }

            task.Step2Status = "Running";

            if (string.IsNullOrWhiteSpace(topicOrUrl))
            {
                throw new ArgumentException("Topic or Video URL cannot be empty for Topic Research Step.");
            }

            string? currentSessionId = existingSessionId;

            if (enableDeepResearch)
            {
                // selectedModel is already the full Gemini model name
                string resolvedModel = GeminiApiService.ResolveModelName(selectedModel);
                logTask(task, $"[STEP 2] Phase 1: Initiating Async Deep Research for topic: '{topicOrUrl}' (Model: {resolvedModel})...");
                
                string researchPrompt = $@"Let's do deep research to : {topicOrUrl}
Research requirement:
1. Analyze from scientific, psychological, and neurobiological perspectives.
2. Cite famous research papers and modern philosophical perspectives.
3. Synthesize a comprehensive and multi-perspective research report.";

                var researchStatus = await _geminiApiService.ExecuteDeepResearchWithProgressAsync(
                    message: researchPrompt,
                    gemId: gemId,
                    model: resolvedModel,
                    sessionId: currentSessionId,
                    onProgress: msg => logTask(task, msg)
                );

                string reportText = researchStatus.text ?? "";
                currentSessionId = researchStatus.session_id;

                if (string.IsNullOrWhiteSpace(reportText))
                {
                    throw new InvalidOperationException("[STEP 2] Gemini API returned empty research report.");
                }

                string reportPath = Path.Combine(outputDir, "research_report.txt");
                await File.WriteAllTextAsync(reportPath, reportText, System.Text.Encoding.UTF8);
                logTask(task, $"[STEP 2] Phase 1 Success! Saved research report ({reportText.Length} chars) to: {reportPath}");

                string targetLangName = !string.IsNullOrWhiteSpace(task.TargetLanguage) ? task.TargetLanguage : "English";
                if (targetLangName.Contains(" - "))
                {
                    targetLangName = targetLangName.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                }

                logTask(task, $"[STEP 2] Phase 2: Converting Research Session context to final spoken script transcript in {targetLangName} (Session ID: {currentSessionId})...");
                
                string scriptPrompt = $"Write a complete, high-quality script of approximately 1600 - 2000 words in {targetLangName} based on these guidelines. Remember: output ONLY the spoken words. NOT INCLUDE: title, [Pause 2 seconds], description, special characters";

                var scriptResponse = await _geminiApiService.SendChatAsync(
                    message: scriptPrompt,
                    gemId: gemId,
                    model: resolvedModel,
                    deepResearch: false,
                    sessionId: currentSessionId
                );

                if (string.IsNullOrWhiteSpace(scriptResponse.text))
                {
                    throw new InvalidOperationException("[STEP 2] Gemini API returned empty script from research session.");
                }

                // ── SAVE THOUGHTS if available ──
                if (!string.IsNullOrWhiteSpace(scriptResponse.thoughts))
                {
                    string thoughtsPath = Path.Combine(outputDir, "transcript_thoughts.txt");
                    await File.WriteAllTextAsync(thoughtsPath, scriptResponse.thoughts, System.Text.Encoding.UTF8);
                    logTask(task, $"[STEP 2] 🧠 Thinking process saved ({scriptResponse.thoughts.Length} chars) → transcript_thoughts.txt");
                }

                string transcriptPathLocal = Path.Combine(outputDir, "transcript.txt");
                await File.WriteAllTextAsync(transcriptPathLocal, scriptResponse.text, System.Text.Encoding.UTF8);
                
                // Also write output_transcript.md for compatibility
                string mdPath = Path.Combine(outputDir, "output_transcript.md");
                await File.WriteAllTextAsync(mdPath, scriptResponse.text, System.Text.Encoding.UTF8);

                task.Step2Status = "Done";
                logTask(task, $"[STEP 2] Success! Saved final transcript ({scriptResponse.text.Length} chars) to: {transcriptPath}");
            }
            else
            {
                string resolvedModel2 = GeminiApiService.ResolveModelName(selectedModel);
                string targetLangName = !string.IsNullOrWhiteSpace(task.TargetLanguage) ? task.TargetLanguage : "English";
                if (targetLangName.Contains(" - "))
                {
                    targetLangName = targetLangName.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim();
                }

                string prompt = $"Write a complete, high-quality video script transcript of approximately 1600 - 2000 words in {targetLangName} about the topic: '{topicOrUrl}'. Remember: output ONLY the spoken words.";
                logTask(task, $"[STEP 2] Sending request to Gemini API (Gem ID: {gemId ?? "Default"}, Model: {resolvedModel2}, Language: {targetLangName})...");

                var response = await _geminiApiService.SendChatAsync(
                    message: prompt,
                    gemId: gemId,
                    model: resolvedModel2,
                    deepResearch: false,
                    sessionId: currentSessionId
                );

                if (string.IsNullOrWhiteSpace(response.text))
                {
                    throw new InvalidOperationException("[STEP 2] Gemini API returned empty transcript.");
                }

                currentSessionId = response.session_id;
                string transcriptPathLocal = Path.Combine(outputDir, "transcript.txt");
                await File.WriteAllTextAsync(transcriptPathLocal, response.text, System.Text.Encoding.UTF8);
                
                string mdPath = Path.Combine(outputDir, "output_transcript.md");
                await File.WriteAllTextAsync(mdPath, response.text, System.Text.Encoding.UTF8);

                task.Step2Status = "Done";
                logTask(task, $"[STEP 2] Success! Saved generated transcript to: {transcriptPath}");
            }

            return currentSessionId;
        }
    }
}
