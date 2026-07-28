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
        private readonly GeminiPlaywrightSceneBreakdownStep _sceneBreakdownStep;
        private readonly SceneImageBatchStep _imageBatchStep;

        public GeminiVideoPipelineService(
            GeminiTopicResearchStep topicResearchStep,
            VoiceoverGenerationStep voiceoverStep,
            GeminiPlaywrightSceneBreakdownStep sceneBreakdownStep,
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
            string? sceneCreatorGemName,
            bool enableDeepResearch,
            string voiceId,
            string? imageGenProvider,
            Action<AutomationTask, string> logTask,
            string? scriptwriterModel = null,
            string? sceneCreatorModel = null)
        {
            task.Status = "Running";
            logTask(task, $"[GEMINI-WORKFLOW] Starting Gemini 5-Step Video Creation Pipeline for: '{topicOrUrl}' (Script: {scriptwriterModel ?? "Default"}, Scene: {sceneCreatorModel ?? "Default"})");

            // Model names are already the full Gemini model names (e.g. "gemini-3-pro")
            string resolvedScriptModel = GeminiApiService.ResolveModelName(scriptwriterModel);
            string resolvedSceneModel = GeminiApiService.ResolveModelName(sceneCreatorModel);

            string outputDir = task.OutputDir;
            Directory.CreateDirectory(outputDir);

            try
            {
                string transcriptPath = Path.Combine(outputDir, "transcript.txt");
                string srtPath = Path.Combine(outputDir, "voiceover.srt");
                string mp3Path = Path.Combine(outputDir, "voiceover.mp3");
                string wavPath = Path.Combine(outputDir, "voiceover.wav");
                string scenesPath = Path.Combine(outputDir, "scenes.json");

                string? activeSessionId = null;

                // Step 1 & 2: Topic Research + Gemini Deep Research Transcript
                if (task.Step2)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 1 & 2: Gemini Deep Research Transcript ---");

                    // Strict primary-folder check only.
                    if (File.Exists(transcriptPath) && new FileInfo(transcriptPath).Length > 50)
                    {
                        logTask(task, $"[STEP 1 & 2] ⏭️ transcript.txt đã tồn tại trong folder hiện tại ({new FileInfo(transcriptPath).Length} bytes). Skipping Step 1 & 2.");
                        task.Step2Status = "Done";
                    }
                    else
                    {
                        activeSessionId = await _topicResearchStep.ExecuteAsync(
                            topicOrUrl: topicOrUrl,
                            outputDir: outputDir,
                            gemId: scriptwriterGemId,
                            enableDeepResearch: enableDeepResearch,
                            task: task,
                            logTask: logTask,
                            selectedModel: resolvedScriptModel,
                            existingSessionId: null
                        );
                    }
                }

                // Read generated transcript
                string scriptText = File.Exists(transcriptPath) ? await File.ReadAllTextAsync(transcriptPath) : string.Empty;

                // Step 3: Voiceover & SRT Subtitles
                if (task.Step4)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 3: AI84 Voiceover & Subtitles ---");
                    // Strict primary-folder check only.
                    bool hasAudio = (File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 1000)
                                 || (File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000);
                    bool hasSrt = File.Exists(srtPath) && new FileInfo(srtPath).Length > 10;

                    if (hasAudio && hasSrt)
                    {
                        logTask(task, "[STEP 3] ⏭️ Voiceover audio + SRT đã tồn tại trong folder hiện tại. Skipping Step 3.");
                        task.Step4Status = "Done";
                        task.StepSrtStatus = "Done";
                    }
                    else
                    {
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
                }
                else if (task.StepSrt)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 3: Whisper SRT Only ---");
                    if (File.Exists(srtPath) && new FileInfo(srtPath).Length > 10)
                    {
                        logTask(task, "[STEP 3] ⏭️ voiceover.srt đã tồn tại trong folder hiện tại. Skipping Whisper SRT step.");
                        task.StepSrtStatus = "Done";
                    }
                    else
                    {
                        await _voiceoverStep.GenerateSrtOnlyAsync(outputDir, task, logTask);
                    }
                }

                // Step 4: Gemini Gem "Scene Creator" JSON Breakdown
                if (task.Step3) // mapped to scene breakdown
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 4: Gemini Scene Creator JSON Breakdown ---");
                    // Strict primary-folder check only — do NOT skip when scenes.json only exists in a sibling folder.
                    if (File.Exists(scenesPath) && new FileInfo(scenesPath).Length > 50 && IsValidScenesJson(scenesPath))
                    {
                        logTask(task, "[STEP 4] ⏭️ scenes.json hợp lệ đã tồn tại trong folder hiện tại. Skipping Step 4.");
                        task.Step3Status = "Done";
                    }
                    else
                    {
                        await _sceneBreakdownStep.ExecuteAsync(
                            outputDir: outputDir,
                            gemId: sceneCreatorGemId,
                            task: task,
                            logTask: logTask,
                            selectedModel: resolvedSceneModel,
                            sessionId: null,
                            gemName: sceneCreatorGemName
                        );
                    }
                }

                // Step 5: Batch Image Generation (Flow Local API)
                if (task.Step5)
                {
                    logTask(task, "[GEMINI-WORKFLOW] --- STEP 5: Scene Batch Image Generation ---");
                    if (AreAllSceneImagesGenerated(scenesPath, outputDir))
                    {
                        logTask(task, "[STEP 5] ⏭️ All scene images already exist in asset folder. Skipping Step 5.");
                        task.Step5Status = "Done";
                    }
                    else
                    {
                        await _imageBatchStep.ExecuteAsync(
                            outputDir: outputDir,
                            providerKey: imageGenProvider ?? "flow_local",
                            task: task,
                            logTask: logTask
                        );
                    }
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

        private bool IsValidScenesJson(string scenesPath)
        {
            try
            {
                string json = File.ReadAllText(scenesPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<ScenesJsonRootModel>(json, options);
                return root != null && root.scenes != null && root.scenes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool AreAllSceneImagesGenerated(string scenesPath, string outputDir)
        {
            try
            {
                if (!File.Exists(scenesPath)) return false;
                string json = File.ReadAllText(scenesPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<ScenesJsonRootModel>(json, options);
                if (root == null || root.scenes == null || root.scenes.Count == 0) return false;

                foreach (var sc in root.scenes)
                {
                    string imgPng = Path.Combine(outputDir, $"{sc.id}.png");
                    string imgJpg = Path.Combine(outputDir, $"{sc.id}.jpg");
                    bool existsPng = File.Exists(imgPng) && new FileInfo(imgPng).Length > 100;
                    bool existsJpg = File.Exists(imgJpg) && new FileInfo(imgJpg).Length > 100;

                    if (!existsPng && !existsJpg)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
