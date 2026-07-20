using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Playwright;

namespace AutoCreateImage
{
    /// <summary>
    /// Orchestrates the automation pipeline by delegating to individual step services.
    /// Reduced from ~1491 lines to ~200 lines by extracting all step logic to Services/Steps/.
    /// </summary>
    public partial class MainWindow : Window
    {
        private async Task RunSingleVideoFlowAsync(AutomationTask task)
        {
            task.Status = "Running";
            LogTask(task, $"[FLOW] Starting parallel flow for video: {task.VideoId}");

            try
            {
                Directory.CreateDirectory(task.OutputDir);
                LogTask(task, $"[FLOW] Outputs will be saved in: {task.OutputDir}");
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                LogTask(task, $"[ERROR] Failed to create output folder: {ex.Message}");
                return;
            }

            // Track the status of the two pipelines to update overall Task status
            string pipeline1Status = task.Step1 ? "Pending Thumbnail" : "Done";
            string pipeline2Status = (task.Step2 || task.Step3 || task.Step4) ? "Pending Script" : "Done";

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
                            LogTask(task, "[IMAGE-BRANCH] Thumbnail already exists. Skipping Step 1.");
                        }
                        else
                        {
                            pipeline1Status = "Step 1: Thumbnail";
                            updateOverallStatus();
                            LogTask(task, "[IMAGE-BRANCH] Starting Step 1: Download Thumbnail...");
                            await _step1.ExecuteAsync(task, LogTask);
                        }
                    }

                    if (task.Step5)
                    {
                        pipeline1Status = "Step 5: Image Gen";
                        updateOverallStatus();
                        LogTask(task, "[IMAGE-BRANCH] Starting Step 5: Image Generation...");
                        await _step5.ExecuteAsync(task, LogTask);
                    }

                    pipeline1Status = "Done";
                    updateOverallStatus();
                }
                catch (Exception ex)
                {
                    pipeline1Status = "Failed";
                    updateOverallStatus();
                    LogTask(task, $"[IMAGE-BRANCH] [ERROR] Branch failed: {ex.Message}");
                    throw;
                }
            });

            // Pipeline 2: Transcript => ChatGPT Rewrite => Voiceover
            var pipeline2Task = Task.Run(async () =>
            {
                string tempProfilePath = "";
                IBrowserContext? context = null;

                try
                {
                    // Browser initialization (only needed for Steps 2 and 3)
                    bool needStep2 = task.Step2 && !File.Exists(Path.Combine(task.OutputDir, "transcript.txt"));
                    bool needStep3 = task.Step3 && !File.Exists(Path.Combine(task.OutputDir, "rewritten_script.txt"));

                    if (needStep2 || needStep3)
                    {
                        string originalProfilePath = Path.Combine(GetProfilesBaseDir(), task.SelectedProfile);
                        tempProfilePath = Path.Combine(Path.GetTempPath(), "AutoCreateImage", $"TempProfile_{task.VideoId}_{Guid.NewGuid()}");

                        LogTask(task, $"[SCRIPT-BRANCH] Cloning Chrome Profile '{task.SelectedProfile}' to temporary folder...");
                        try
                        {
                            if (Directory.Exists(originalProfilePath))
                            {
                                _browserService.CopyProfileDirectory(originalProfilePath, tempProfilePath);
                            }
                            else
                            {
                                Directory.CreateDirectory(tempProfilePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTask(task, $"[SCRIPT-BRANCH] [WARNING] Profile cloning failed: {ex.Message}. Running with fresh profile.");
                            Directory.CreateDirectory(tempProfilePath);
                        }

                        context = await _browserService.EnsureBrowserInitializedAsync(tempProfilePath);
                    }

                    // Step 2: Extract Transcript
                    string? transcript = null;
                    string? rewrittenScript = null;
                    if (task.Step2)
                    {
                        string path = Path.Combine(task.OutputDir, "transcript.txt");
                        if (File.Exists(path))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] transcript.txt already exists. Skipping extraction.");
                            transcript = await File.ReadAllTextAsync(path);
                        }
                        else
                        {
                            pipeline2Status = "Step 2: Transcript";
                            updateOverallStatus();
                            LogTask(task, "[SCRIPT-BRANCH] Starting Step 2: Transcript Extraction...");
                            transcript = await _step2.ExecuteAsync(task, context!, LogTask);
                        }
                    }
                    else if (task.Step3)
                    {
                        string path = Path.Combine(task.OutputDir, "transcript.txt");
                        if (File.Exists(path))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] Loading transcript from file transcript.txt...");
                            transcript = await File.ReadAllTextAsync(path);
                        }
                    }

                    // Step 3: Rewrite Script (ChatGPT)
                    if (task.Step3)
                    {
                        string path = Path.Combine(task.OutputDir, "rewritten_script.txt");
                        if (File.Exists(path))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] rewritten_script.txt already exists. Skipping ChatGPT rewrite.");
                            rewrittenScript = await File.ReadAllTextAsync(path);
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(transcript))
                            {
                                string transPath = Path.Combine(task.OutputDir, "transcript.txt");
                                if (File.Exists(transPath))
                                {
                                    transcript = await File.ReadAllTextAsync(transPath);
                                }
                            }
                            if (string.IsNullOrWhiteSpace(transcript))
                            {
                                throw new Exception("Transcript is empty. Cannot run Step 3.");
                            }
                            pipeline2Status = "Step 3: ChatGPT";
                            updateOverallStatus();
                            LogTask(task, "[SCRIPT-BRANCH] Starting Step 3: ChatGPT Rewrite...");
                            rewrittenScript = await _step3.ExecuteAsync(task.TargetLanguage, task.OutputDir, transcript, task.VideoId, task, context!, LogTask);
                        }
                    }
                    else if (task.Step4)
                    {
                        string path = Path.Combine(task.OutputDir, "rewritten_script.txt");
                        if (File.Exists(path))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] Loading script from rewritten_script.txt...");
                            rewrittenScript = await File.ReadAllTextAsync(path);
                        }
                    }

                    // Done with browser — close safely
                    if (context != null)
                    {
                        LogTask(task, "[SCRIPT-BRANCH] Closing browser instance...");
                        await _browserService.CloseBrowserSafelyAsync(context, tempProfilePath, (msg) => LogTask(task, $"[SCRIPT-BRANCH] {msg}"));
                        context = null;
                    }

                    _browserService.CleanupTempProfile(tempProfilePath, (msg) => LogTask(task, $"[SCRIPT-BRANCH] {msg}"));
                    tempProfilePath = "";

                    // Step 4: Generate Voiceover (AI84 API)
                    if (task.Step4)
                    {
                        string voiceoverPath = Path.Combine(task.OutputDir, "voiceover.mp3");
                        if (File.Exists(voiceoverPath))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] voiceover.mp3 already exists. Skipping Voiceover generation.");
                        }
                        else
                        {
                            if (string.IsNullOrWhiteSpace(rewrittenScript))
                            {
                                string rewPath = Path.Combine(task.OutputDir, "rewritten_script.txt");
                                if (File.Exists(rewPath))
                                {
                                    rewrittenScript = await File.ReadAllTextAsync(rewPath);
                                }
                            }
                            if (string.IsNullOrWhiteSpace(rewrittenScript))
                            {
                                throw new Exception("Rewritten script is empty. Cannot run Step 4.");
                            }
                            pipeline2Status = "Step 4: Voiceover";
                            updateOverallStatus();
                            LogTask(task, "[SCRIPT-BRANCH] Starting Step 4: Voiceover Generation...");
                            string apiKey = ConfigService.CurrentSettings.Ai84ApiKey;
                            await _step4.ExecuteAsync(task.VoiceId, task.OutputDir, rewrittenScript, task.VideoId, task, apiKey, LogTask);
                        }
                    }

                    pipeline2Status = "Done";
                    updateOverallStatus();
                }
                catch (Exception ex)
                {
                    pipeline2Status = "Failed";
                    updateOverallStatus();
                    LogTask(task, $"[SCRIPT-BRANCH] [ERROR] Branch failed: {ex.Message}");
                    throw;
                }
                finally
                {
                    // Ensure cleanup in case of failure
                    if (context != null)
                    {
                        await _browserService.CloseBrowserSafelyAsync(context, tempProfilePath, (msg) => LogTask(task, $"[SCRIPT-BRANCH] {msg}"));
                    }
                    _browserService.CleanupTempProfile(tempProfilePath, (msg) => LogTask(task, $"[SCRIPT-BRANCH] {msg}"));
                }
            });

            // Wait for both pipelines
            await Task.WhenAll(pipeline1Task, pipeline2Task);

            if (pipeline1Status == "Done" && pipeline2Status == "Done")
            {
                task.Status = "Done";
                LogTask(task, "[FLOW] Parallel flow completed successfully!");
            }
            else
            {
                task.Status = "Failed";
                LogTask(task, "[FLOW] Parallel flow finished with failures.");
            }
        }

        /// <summary>
        /// Copies and saves an image from the browser clipboard.
        /// Kept here as it requires WPF Dispatcher access.
        /// </summary>
        private async Task<bool> CopyAndSaveFromClipboardAsync(IPage page, string savePath, AutomationTask task)
        {
            try
            {
                var copyBtn = page.Locator("button[data-testid='copy-turn-action-button']").Last;
                LogTask(task, "[STEP 5] Waiting for Copy response button...");
                await copyBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

                LogTask(task, "[STEP 5] Clicking ChatGPT response Copy button...");
                await copyBtn.ClickAsync();
                await Task.Delay(2000);

                bool success = Dispatcher.Invoke(() =>
                {
                    if (System.Windows.Clipboard.ContainsImage())
                    {
                        var image = System.Windows.Clipboard.GetImage();
                        if (image != null)
                        {
                            using (var fileStream = new FileStream(savePath, FileMode.Create))
                            {
                                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                                encoder.Save(fileStream);
                            }
                            return true;
                        }
                    }
                    return false;
                });

                if (success)
                {
                    LogTask(task, $"[STEP 5] Successfully copied and saved image from clipboard to: {savePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogTask(task, $"[STEP 5] [WARNING] Clipboard copy failed: {ex.Message}");
            }
            return false;
        }
    }
}
