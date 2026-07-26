using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Encapsulates the legacy YouTube Re-use automation workflow (Thumbnail -> Transcript -> ChatGPT Rewrite -> Voiceover/SRT -> Image Edit).
    /// Decoupled from UI code-behind for Clean Architecture.
    /// </summary>
    public class LegacyVideoPipelineService
    {
        private readonly ThumbnailDownloadStep _step1;
        private readonly TranscriptExtractionStep _step2;
        private readonly ChatGptRewriteStep _step3;
        private readonly VoiceoverGenerationStep _step4;
        private readonly ImageGenerationStep _step5;

        public LegacyVideoPipelineService(
            ThumbnailDownloadStep step1,
            TranscriptExtractionStep step2,
            ChatGptRewriteStep step3,
            VoiceoverGenerationStep step4,
            ImageGenerationStep step5)
        {
            _step1 = step1;
            _step2 = step2;
            _step3 = step3;
            _step4 = step4;
            _step5 = step5;
        }

        public async Task ExecutePipelineAsync(
            AutomationTask task,
            IBrowserContext? browserContext,
            IPage? chatGptPage,
            Action<AutomationTask, string> logTask)
        {
            task.Status = "Running";
            task.Step1Status = task.Step1 ? "Pending" : "Pending";
            task.Step2Status = task.Step2 ? "Pending" : "Pending";
            task.Step3Status = task.Step3 ? "Pending" : "Pending";
            task.Step4Status = task.Step4 ? "Pending" : "Pending";
            task.StepSrtStatus = task.StepSrt ? "Pending" : "Pending";
            task.Step5Status = task.Step5 ? "Pending" : "Pending";

            logTask(task, $"[LEGACY-FLOW] Starting pipeline for video: {task.VideoId}");

            try
            {
                Directory.CreateDirectory(task.OutputDir);
                logTask(task, $"[LEGACY-FLOW] Outputs directory: {task.OutputDir}");
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                logTask(task, $"[ERROR] Failed to create output folder: {ex.Message}");
                return;
            }

            string pipeline1Status = (task.Step1 || task.Step5) ? "Pending Thumbnail/Image" : "Done";
            string pipeline2Status = (task.Step2 || task.Step3 || task.Step4 || task.StepSrt) ? "Pending Script/Voice" : "Done";

            var updateOverallStatus = new Action(() =>
            {
                if (pipeline1Status == "Failed" || pipeline2Status == "Failed")
                {
                    task.Status = "Failed";
                }
                else if (pipeline1Status == "Done" && pipeline2Status == "Done")
                {
                    task.Status = "Done";
                }
                else
                {
                    task.Status = $"{pipeline1Status} | {pipeline2Status}";
                }
            });

            // Pipeline 1: Thumbnail => Image Generation
            var pipeline1Task = Task.Run(async () =>
            {
                try
                {
                    if (task.Step1)
                    {
                        string thumbnailPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");
                        if (File.Exists(thumbnailPath))
                        {
                            logTask(task, "[IMAGE-BRANCH] Thumbnail already exists. Skipping Step 1.");
                            task.Step1Status = "Done";
                        }
                        else
                        {
                            try
                            {
                                pipeline1Status = "Step 1: Thumbnail";
                                updateOverallStatus();
                                task.Step1Status = "Running";
                                logTask(task, "[IMAGE-BRANCH] Starting Step 1: Download Thumbnail...");
                                await _step1.ExecuteAsync(task, logTask);
                                task.Step1Status = "Done";
                            }
                            catch
                            {
                                task.Step1Status = "Failed";
                                throw;
                            }
                        }
                    }

                    if (task.Step5)
                    {
                        try
                        {
                            pipeline1Status = "Step 5: Image Gen";
                            updateOverallStatus();
                            task.Step5Status = "Running";
                            logTask(task, "[IMAGE-BRANCH] Starting Step 5: Image Generation...");
                            await _step5.ExecuteAsync(task, logTask);
                            task.Step5Status = "Done";
                        }
                        catch
                        {
                            task.Step5Status = "Failed";
                            throw;
                        }
                    }

                    pipeline1Status = "Done";
                    updateOverallStatus();
                }
                catch (Exception ex)
                {
                    pipeline1Status = "Failed";
                    updateOverallStatus();
                    logTask(task, $"[ERROR] [IMAGE-BRANCH] Failed: {ex.Message}");
                }
            });

            // Pipeline 2: Transcript => Rewrite => Voiceover => Subtitles
            var pipeline2Task = Task.Run(async () =>
            {
                try
                {
                    string? rawTranscript = null;
                    if (task.Step2)
                    {
                        string rawTranscriptPath = Path.Combine(task.OutputDir, "transcript.txt");
                        if (File.Exists(rawTranscriptPath))
                        {
                            logTask(task, "[SCRIPT-BRANCH] transcript.txt already exists. Skipping Step 2.");
                            rawTranscript = await File.ReadAllTextAsync(rawTranscriptPath);
                            task.Step2Status = "Done";
                        }
                        else
                        {
                            try
                            {
                                pipeline2Status = "Step 2: Extract Transcript";
                                updateOverallStatus();
                                task.Step2Status = "Running";
                                logTask(task, "[SCRIPT-BRANCH] Starting Step 2: Extract Transcript...");
                                rawTranscript = await _step2.ExecuteAsync(task, browserContext!, logTask);
                                task.Step2Status = "Done";
                            }
                            catch
                            {
                                task.Step2Status = "Failed";
                                throw;
                            }
                        }
                    }

                    string scriptText = string.Empty;
                    if (task.Step3)
                    {
                        try
                        {
                            pipeline2Status = "Step 3: ChatGPT Rewrite";
                            updateOverallStatus();
                            task.Step3Status = "Running";
                            logTask(task, "[SCRIPT-BRANCH] Starting Step 3: ChatGPT Transcript Rewrite...");
                            scriptText = (await _step3.ExecuteAsync(task.TargetLanguage, task.OutputDir, rawTranscript ?? string.Empty, task.VideoId, task, browserContext!, logTask)) ?? string.Empty;
                            task.Step3Status = "Done";
                        }
                        catch
                        {
                            task.Step3Status = "Failed";
                            throw;
                        }
                    }
                    else
                    {
                        string rawTranscriptPath = Path.Combine(task.OutputDir, "transcript.txt");
                        string rewrittenTranscriptPath = Path.Combine(task.OutputDir, "rewritten_script.txt");

                        if (File.Exists(rewrittenTranscriptPath))
                        {
                            scriptText = await File.ReadAllTextAsync(rewrittenTranscriptPath);
                        }
                        else if (File.Exists(rawTranscriptPath))
                        {
                            scriptText = await File.ReadAllTextAsync(rawTranscriptPath);
                        }
                    }

                    if (task.Step4)
                    {
                        try
                        {
                            pipeline2Status = "Step 4: Voiceover (AI84)";
                            updateOverallStatus();
                            task.Step4Status = "Running";
                            logTask(task, "[SCRIPT-BRANCH] Starting Step 4: Asynchronous Voiceover creation via AI84...");
                            await _step4.ExecuteAsync(task.VoiceId, task.OutputDir, scriptText, task.VideoId, task, ConfigService.CurrentSettings.Ai84ApiKey, logTask);
                            task.Step4Status = "Done";
                        }
                        catch
                        {
                            task.Step4Status = "Failed";
                            throw;
                        }
                    }
                    else if (task.StepSrt)
                    {
                        try
                        {
                            pipeline2Status = "Step SRT: Whisper Transcription";
                            updateOverallStatus();
                            task.StepSrtStatus = "Running";
                            logTask(task, "[SCRIPT-BRANCH] Step 4 is disabled, but SRT is enabled. Running Whisper SRT generation...");
                            await _step4.GenerateSrtOnlyAsync(task.OutputDir, task, logTask);
                            task.StepSrtStatus = "Done";
                        }
                        catch
                        {
                            task.StepSrtStatus = "Failed";
                            throw;
                        }
                    }

                    pipeline2Status = "Done";
                    updateOverallStatus();
                }
                catch (Exception ex)
                {
                    pipeline2Status = "Failed";
                    updateOverallStatus();
                    logTask(task, $"[ERROR] [SCRIPT-BRANCH] Failed: {ex.Message}");
                }
            });

            await Task.WhenAll(pipeline1Task, pipeline2Task);
        }
    }
}
