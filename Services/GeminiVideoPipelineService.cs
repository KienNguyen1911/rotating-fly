using System;
using System.IO;
using System.Threading.Tasks;
using AssetAutomator.Services;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Encapsulates the new Gemini Deep Research Video Automation workflow (todo.md):
    /// Step 1/2: Topic Research & Gemini Deep Research Transcript
    /// Step 3: AI84 Voiceover & Whisper SRT Subtitles
    /// Step 4: Gemini Gem "Scene Creator" JSON Breakdown
    /// Step 5: Batch Image Generation (Flow Local API)
    /// </summary>
    public class GeminiVideoPipelineService
    {
        private readonly GeminiTopicResearchStep _topicResearchStep;
        private readonly VoiceoverGenerationStep _voiceoverStep;
        private readonly GeminiSceneBreakdownStep _sceneBreakdownStep;
        private readonly SceneImageBatchStep _imageBatchStep;

        public GeminiVideoPipelineService(
            GeminiTopicResearchStep topicResearchStep,
            VoiceoverGenerationStep voiceoverStep,
            GeminiSceneBreakdownStep sceneBreakdownStep,
            SceneImageBatchStep imageBatchStep)
        {
            _topicResearchStep = topicResearchStep;
            _voiceoverStep = voiceoverStep;
            _sceneBreakdownStep = sceneBreakdownStep;
            _imageBatchStep = imageBatchStep;
        }

        public async Task ExecutePipelineAsync(
            AutomationTask task,
            string topicOrUrl,
            string? scriptwriterGemId,
            string? sceneCreatorGemId,
            bool enableDeepResearch,
            string voiceId,
            string? imageGenProvider,
            Action<AutomationTask, string> logTask)
        {
            task.Status = "Running";
            logTask(task, $"[GEMINI-WORKFLOW] Starting Gemini 5-Step Video Creation Pipeline for: '{topicOrUrl}'");

            string outputDir = task.OutputDir;
            Directory.CreateDirectory(outputDir);

            try
            {
                // Step 1 & 2: Topic Research + Gemini Deep Research Transcript
                if (task.Step2)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 1 & 2: Gemini Deep Research Transcript ---");
                    await _topicResearchStep.ExecuteAsync(
                        topicOrUrl: topicOrUrl,
                        outputDir: outputDir,
                        gemId: scriptwriterGemId,
                        enableDeepResearch: enableDeepResearch,
                        task: task,
                        logTask: logTask
                    );
                }

                // Read generated transcript
                string transcriptPath = Path.Combine(outputDir, "transcript.txt");
                string scriptText = File.Exists(transcriptPath) ? await File.ReadAllTextAsync(transcriptPath) : string.Empty;

                // Step 3: Voiceover & SRT Subtitles
                if (task.Step4)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 3: AI84 Voiceover & Subtitles ---");
                    await _voiceoverStep.ExecuteAsync(
                        voiceId: voiceId,
                        outputDir: outputDir,
                        scriptText: scriptText,
                        videoId: task.VideoId,
                        task: task,
                        apiKey: ConfigService.CurrentSettings.Ai84ApiKey,
                        logTask: logTask
                    );
                }
                else if (task.StepSrt)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 3: Whisper SRT Only ---");
                    await _voiceoverStep.GenerateSrtOnlyAsync(outputDir, task, logTask);
                }

                // Step 4: Gemini Gem "Scene Creator" JSON Breakdown
                if (task.Step3) // mapped to scene breakdown
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 4: Gemini Scene Creator JSON Breakdown ---");
                    await _sceneBreakdownStep.ExecuteAsync(
                        outputDir: outputDir,
                        gemId: sceneCreatorGemId,
                        task: task,
                        logTask: logTask
                    );
                }

                // Step 5: Batch Image Generation (Flow Local API)
                if (task.Step5)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 5: Scene Batch Image Generation ---");
                    await _imageBatchStep.ExecuteAsync(
                        outputDir: outputDir,
                        providerKey: imageGenProvider ?? "flow_local",
                        task: task,
                        logTask: logTask
                    );
                }

                task.Status = "Done";
                logTask(task, "[GEMINI-WORKFLOW] 🎉 Pipeline completed successfully!");
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                logTask(task, $"[ERROR] [GEMINI-WORKFLOW] Pipeline failed: {ex.Message}");
                throw;
            }
        }
    }
}
