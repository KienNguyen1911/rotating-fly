using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Playwright;
using YoutubeExplode;
using YoutubeExplode.Videos.ClosedCaptions;

namespace AutoCreateImage
{
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

            // We will track the status of the two pipelines to update the overall Task status dynamically
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

            // Pipeline 1: Get Thumbnail => Create Images at Step 5
            var pipeline1Task = Task.Run(async () =>
            {
                try
                {
                    // Step 1: Thumbnail Download (Direct HTTP)
                    if (task.Step1)
                    {
                        pipeline1Status = "Step 1: Thumbnail";
                        updateOverallStatus();
                        LogTask(task, "[IMAGE-BRANCH] Starting Step 1: Download Thumbnail...");
                        await RunStep1Async(task);
                    }

                    // Step 5: Generate Images (Image Edits API)
                    if (task.Step5)
                    {
                        pipeline1Status = "Step 5: Image Gen";
                        updateOverallStatus();
                        LogTask(task, "[IMAGE-BRANCH] Starting Step 5: Image Generation...");
                        await RunStep5Async(task);
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

            // Pipeline 2: Get Script => Rewrite Script => Voice-over
            var pipeline2Task = Task.Run(async () =>
            {
                string tempProfilePath = "";
                IBrowserContext? context = null;

                try
                {
                    // Playwright initialization (Only steps 2 and 3 require browser automation)
                    if (task.Step2 || task.Step3)
                    {
                        string originalProfilePath = Path.Combine(GetProfilesBaseDir(), task.SelectedProfile);
                        tempProfilePath = Path.Combine(Path.GetTempPath(), "AutoCreateImage", $"TempProfile_{task.VideoId}_{Guid.NewGuid()}");

                        LogTask(task, $"[SCRIPT-BRANCH] Cloning Chrome Profile '{task.SelectedProfile}' to temporary folder...");
                        try
                        {
                            if (Directory.Exists(originalProfilePath))
                            {
                                CopyProfileDirectory(originalProfilePath, tempProfilePath);
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

                        context = await EnsureBrowserInitializedAsync(tempProfilePath);
                    }

                    // Step 2: Extract Transcript
                    string? transcript = null;
                    string? rewrittenScript = null;
                    if (task.Step2)
                    {
                        pipeline2Status = "Step 2: Transcript";
                        updateOverallStatus();
                        LogTask(task, "[SCRIPT-BRANCH] Starting Step 2: Transcript Extraction...");
                        transcript = await RunStep2Async(task, context!);
                    }
                    else if (task.Step3)
                    {
                        // Fallback to load transcript from file
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
                        if (string.IsNullOrWhiteSpace(transcript))
                        {
                            throw new Exception("Transcript is empty. Cannot run Step 3.");
                        }
                        pipeline2Status = "Step 3: ChatGPT";
                        updateOverallStatus();
                        LogTask(task, "[SCRIPT-BRANCH] Starting Step 3: ChatGPT Rewrite...");
                        rewrittenScript = await RunStep3Async(task.TargetLanguage, task.OutputDir, transcript, task.VideoId, task, context!);
                    }
                    else if (task.Step4)
                    {
                        // Fallback to load rewritten script from file
                        string path = Path.Combine(task.OutputDir, "rewritten_script.txt");
                        if (File.Exists(path))
                        {
                            LogTask(task, "[SCRIPT-BRANCH] Loading script from rewritten_script.txt...");
                            rewrittenScript = await File.ReadAllTextAsync(path);
                        }
                    }

                    // Done with browser now
                    if (context != null)
                    {
                        LogTask(task, "[SCRIPT-BRANCH] Closing browser instance...");
                        // Remove from dictionary first to prevent Close event handler interference
                        _browserContexts.TryRemove(tempProfilePath, out _);
                        try
                        {
                            // Use timeout to prevent hanging if browser is unresponsive
                            var closeTask = context.CloseAsync();
                            if (await Task.WhenAny(closeTask, Task.Delay(15000)) != closeTask)
                            {
                                LogTask(task, "[SCRIPT-BRANCH] Browser close timed out after 15s, continuing anyway...");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTask(task, $"[SCRIPT-BRANCH] Browser close error (non-fatal): {ex.Message}");
                        }
                        context = null;
                        LogTask(task, "[SCRIPT-BRANCH] Browser instance closed successfully.");
                    }

                    if (!string.IsNullOrEmpty(tempProfilePath) && Directory.Exists(tempProfilePath))
                    {
                        LogTask(task, "[SCRIPT-BRANCH] Cleaning up temporary profile folder...");
                        try
                        {
                            Directory.Delete(tempProfilePath, true);
                        }
                        catch { }
                        tempProfilePath = "";
                    }

                    // Step 4: Generate Voiceover (ai84.pro)
                    if (task.Step4)
                    {
                        if (string.IsNullOrWhiteSpace(rewrittenScript))
                        {
                            throw new Exception("Rewritten script is empty. Cannot run Step 4.");
                        }
                        pipeline2Status = "Step 4: Voiceover";
                        updateOverallStatus();
                        LogTask(task, "[SCRIPT-BRANCH] Starting Step 4: Voiceover Generation...");
                        string apiKey = "";
                        Dispatcher.Invoke(() => apiKey = TxtAi84ApiKey.Text.Trim());
                        await RunStep4Async(task.VoiceId, task.OutputDir, rewrittenScript, task.VideoId, task, apiKey);
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
                    if (context != null)
                    {
                        LogTask(task, "[SCRIPT-BRANCH] Closing browser instance in finally...");
                        _browserContexts.TryRemove(tempProfilePath, out _);
                        try
                        {
                            var closeTask = context.CloseAsync();
                            if (await Task.WhenAny(closeTask, Task.Delay(15000)) != closeTask)
                            {
                                LogTask(task, "[SCRIPT-BRANCH] Browser close timed out in finally, continuing...");
                            }
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(tempProfilePath) && Directory.Exists(tempProfilePath))
                    {
                        LogTask(task, "[SCRIPT-BRANCH] Cleaning up temporary profile folder in finally...");
                        try
                        {
                            Directory.Delete(tempProfilePath, true);
                        }
                        catch { }
                    }
                }
            });

            // Wait for both pipelines to complete
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

        private void CopyProfileDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destinationDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, dest, true);
                }
                catch { }
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                if (dirName.Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Code Cache", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("GPUCache", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string dest = Path.Combine(destinationDir, dirName);
                CopyProfileDirectory(subDir, dest);
            }
        }

        private async Task<IBrowserContext> EnsureBrowserInitializedAsync(string profilePath)
        {
            await _browserInitSemaphore.WaitAsync();
            try
            {
                if (_browserContexts.TryGetValue(profilePath, out var existingContext))
                {
                    Log("Reusing existing browser session for profile: " + Path.GetFileName(profilePath));
                    return existingContext;
                }

                Log("Initializing browser session for profile: " + Path.GetFileName(profilePath));
                if (_playwright == null)
                {
                    _playwright = await Playwright.CreateAsync();
                }

                Log($"Launching browser using Chrome profile path: {profilePath}");
                if (!Directory.Exists(profilePath))
                {
                    Log($"[WARNING] Profile path does not exist. Creating directory: {profilePath}");
                    Directory.CreateDirectory(profilePath);
                }

                try
                {
                    var browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(
                        profilePath,
                        new BrowserTypeLaunchPersistentContextOptions
                        {
                            Headless = false,
                            Channel = "chrome",
                            Args = new[] { 
                                "--disable-blink-features=AutomationControlled",
                                "--no-sandbox",
                                "--disable-infobars"
                            }
                        });

                     // Anti-bot detection script injection
                     await browserContext.AddInitScriptAsync(@"
                         Object.defineProperty(navigator, 'webdriver', {
                             get: () => undefined
                         });
                      ");
     
                      // Handle manual browser close event to release driver processes
                      browserContext.Close += (sender, e) =>
                      {
                          Log($"Browser window closed for profile: {Path.GetFileName(profilePath)}. Cleaning up resources...");
                          _browserContexts.TryRemove(profilePath, out _);
                      };
      
                      _browserContexts[profilePath] = browserContext;
                      Log("Browser session initialized successfully.");
                      return browserContext;
                }
                catch (Exception ex)
                {
                    Log($"[ERROR] Failed to start browser. Make sure Chrome is closed if using a personal profile. Detail: {ex.Message}");
                    throw;
                }
            }
            finally
            {
                _browserInitSemaphore.Release();
            }
        }

        private async Task RunStep1Async(AutomationTask task)
        {
            LogTask(task, "[STEP 1] Starting download thumbnail...");
            string targetUrl = $"https://img.youtube.com/vi/{task.VideoId}/maxresdefault.jpg";
            string fallbackUrl = $"https://img.youtube.com/vi/{task.VideoId}/hqdefault.jpg";
            string outputPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");

            using var httpClient = new HttpClient();
            try
            {
                LogTask(task, $"[STEP 1] Trying to download maxresdefault: {targetUrl}");
                var response = await httpClient.GetAsync(targetUrl);
                if (!response.IsSuccessStatusCode)
                {
                    LogTask(task, $"[STEP 1] Maxresdefault not available. Downloading fallback: {fallbackUrl}");
                    response = await httpClient.GetAsync(fallbackUrl);
                }

                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(outputPath, bytes);
                    LogTask(task, $"[STEP 1] Success! Saved thumbnail to: {outputPath}");
                }
                else
                {
                    LogTask(task, $"[STEP 1] [ERROR] Failed to retrieve thumbnail image from YouTube API.");
                }
            }
            catch (Exception ex)
            {
                LogTask(task, $"[STEP 1] [ERROR] Thumbnail download failed: {ex.Message}");
            }
        }

        private async Task<string?> RunStep2Async(AutomationTask task, IBrowserContext context)
        {
            LogTask(task, "[STEP 2] Starting transcript extraction via YoutubeExplode...");
            string? transcriptText = null;
            try
            {
                var youtube = new YoutubeClient();
                LogTask(task, $"Fetching caption manifest for video: {task.VideoId}");
                var trackManifest = await youtube.Videos.ClosedCaptions.GetManifestAsync(task.VideoId);

                if (trackManifest == null || !trackManifest.Tracks.Any())
                {
                    LogTask(task, "[ERROR] No closed caption tracks found for this video.");
                    return null;
                }

                // Try to find target language or default tracks
                LogTask(task, "Selecting best caption track...");
                string targetLangCode = "en";
                if (task.TargetLanguage.Contains(" - "))
                {
                    targetLangCode = task.TargetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[1].Trim();
                }
                else if (task.TargetLanguage.StartsWith("Viet", StringComparison.OrdinalIgnoreCase))
                {
                    targetLangCode = "vi";
                }
                var trackInfo = trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals(targetLangCode, StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Name.Contains(task.TargetLanguage.Contains(" - ") ? task.TargetLanguage.Split(new[] { " - " }, StringSplitOptions.None)[0].Trim() : task.TargetLanguage, StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("vi", StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault(t => t.Language.Code.Equals("en", StringComparison.OrdinalIgnoreCase))
                             ?? trackManifest.Tracks.FirstOrDefault();

                if (trackInfo == null)
                {
                    LogTask(task, "[ERROR] Could not find any suitable caption track.");
                    return null;
                }

                LogTask(task, $"Selected caption track: {trackInfo.Language.Name} ({trackInfo.Language.Code})");
                var track = await youtube.Videos.ClosedCaptions.GetAsync(trackInfo);
                
                var segments = track.Captions.Select(c => c.Text);
                transcriptText = string.Join(" ", segments);

                if (!string.IsNullOrWhiteSpace(transcriptText))
                {
                    LogTask(task, $"Success! Retrieved transcript length: {transcriptText.Length} characters.");
                    string outputPath = Path.Combine(task.OutputDir, "transcript.txt");
                    await File.WriteAllTextAsync(outputPath, transcriptText);
                    LogTask(task, $"Saved transcript text to: {outputPath}");
                }
                else
                {
                    LogTask(task, "[ERROR] Transcript was empty.");
                }
            }
            catch (Exception ex)
            {
                LogTask(task, $"[ERROR] Transcript extraction failed: {ex.Message}");
                throw;
            }

            return transcriptText;
        }

        private async Task<string?> RunStep3Async(string targetLanguage, string outputDir, string transcriptText, string videoId, AutomationTask task, IBrowserContext context)
        {
            LogTask(task, "[STEP 3] Starting ChatGPT rewrite...");
            if (context == null) throw new InvalidOperationException("Browser not initialized.");

            var page = await context.NewPageAsync();
            string? scriptText = null;
            try
            {
                LogTask(task, "Navigating to ChatGPT 'Dịch chay' Custom GPT...");
                await page.GotoAsync("https://chatgpt.com/g/g-6a4083a0e37081919a248ef7721dae3d-dich-chay");
                await Task.Delay(4000); // Delay to allow full load

                LogTask(task, "Preparing transcript file for drag & drop...");
                string transcriptPath = Path.Combine(outputDir, "transcript.txt");
                if (!File.Exists(transcriptPath))
                {
                    await File.WriteAllTextAsync(transcriptPath, transcriptText);
                }

                await Task.Delay(4000); // Delay to allow full load

                // Read file as Base64 to transfer to browser context
                byte[] fileBytes = await File.ReadAllBytesAsync(transcriptPath);
                string base64File = Convert.ToBase64String(fileBytes);

                await Task.Delay(4000); // Delay to allow full load

                LogTask(task, "Simulating drag & drop of transcript.txt onto ChatGPT page...");
                await page.EvaluateAsync(@"(args) => {
                    const base64Data = args.base64;
                    const fileName = args.name;
                    const raw = atob(base64Data);
                    const rawLength = raw.length;
                    const array = new Uint8Array(new ArrayBuffer(rawLength));
                    for(let i = 0; i < rawLength; i++) {
                        array[i] = raw.charCodeAt(i);
                    }
                    const file = new File([array], fileName, { type: 'text/plain' });
                    const dataTransfer = new DataTransfer();
                    dataTransfer.items.add(file);

                    const target = document.querySelector('#prompt-textarea') || document.body;
                    target.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer }));
                    target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer }));
                    target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer }));

                    // Dispatch dragleave to dismiss the overlay on all levels
                    target.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));
                    document.body.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));
                    window.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));
                    document.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));

                    // Programmatically find and remove the drag overlay elements if they get stuck
                    const cleanOverlay = () => {
                        const allElements = document.querySelectorAll('*');
                        for (const el of allElements) {
                            const style = window.getComputedStyle(el);
                            if ((style.position === 'fixed' || style.position === 'absolute') && 
                                (el.textContent && (el.textContent.includes('Add anything') || el.textContent.includes('Drop any file')))) {
                                el.remove();
                            }
                        }
                    };
                    cleanOverlay();
                    setTimeout(cleanOverlay, 500);
                    setTimeout(cleanOverlay, 1500);
                }", new { base64 = base64File, name = "transcript.txt" });

                LogTask(task, "Drag & Drop simulated, waiting 4 seconds for upload processing...");
                await Task.Delay(4000);

                LogTask(task, "Finding prompt input box to enter instruction...");
                var promptBox = page.Locator("div#prompt-textarea, div[contenteditable='true']").First;
                await promptBox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                await HumanBehaviourHelper.RandomMouseMovementAsync(page);
                await Task.Delay(1500); // Delay before inputting

                string promptText = $"Hãy viết lại kịch bản sau đây bằng ngôn ngữ '{targetLanguage}' , chỉ dịch, không chỉnh sửa nội dung sẵn có trong script";
                LogTask(task, "Typing prompt using Human Typing simulator...");
                await HumanBehaviourHelper.TypeLikeHumanAsync(page, promptBox, promptText);
                await Task.Delay(2000); // Human delay before click send

                LogTask(task, "Submitting prompt...");
                var sendButton = page.Locator("button[data-testid='send-button'], button[aria-label='Send prompt']").First;
                await sendButton.ClickAsync();
                await Task.Delay(5000); // Wait for generation to start

                LogTask(task, "Waiting for AI to finish writing script...");
                int timeoutMs = 90000;
                int elapsed = 0;
                while (elapsed < timeoutMs)
                {
                    var isGenerating = await page.Locator("button[data-testid='stop-button'], button[aria-label='Stop generating']").CountAsync() > 0;
                    if (!isGenerating)
                    {
                        break;
                    }
                    await Task.Delay(2000);
                    elapsed += 2000;
                }

                await Task.Delay(2000);

                LogTask(task, "Extracting the ChatGPT response...");
                var articles = page.Locator("article");
                var count = await articles.CountAsync();
                if (count > 0)
                {
                    var lastArticle = articles.Nth(count - 1);
                    var contentLocator = lastArticle.Locator(".markdown, div[class*='content']").First;
                    scriptText = await contentLocator.InnerTextAsync();
                }
                else
                {
                    var responses = page.Locator("div.agent-turn");
                    var resCount = await responses.CountAsync();
                    if (resCount > 0)
                    {
                        scriptText = await responses.Nth(resCount - 1).InnerTextAsync();
                    }
                }

                if (!string.IsNullOrWhiteSpace(scriptText))
                {
                    LogTask(task, $"Success! Generated script length: {scriptText.Length} characters.");
                    string outputPath = Path.Combine(outputDir, "rewritten_script.txt");
                    await File.WriteAllTextAsync(outputPath, scriptText);
                    LogTask(task, $"Saved rewritten script to: {outputPath}");
                }
                else
                {
                    LogTask(task, "[ERROR] Failed to extract rewritten script content.");
                }
            }
            catch (Exception ex)
            {
                LogTask(task, $"[ERROR] ChatGPT execution failed: {ex.Message}");
                throw;
            }
            finally
            {
                await page.CloseAsync();
            }

            return scriptText;
        }

        private async Task RunStep4Async(string voiceId, string outputDir, string scriptText, string videoId, AutomationTask task, string apiKey)
        {
            LogTask(task, "[STEP 4] Starting Asynchronous Voiceover creation via AI84 API...");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("AI84 API Key is empty. Please enter your API Key in the top toolbar.");
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);

            var requestBody = new
            {
                text = scriptText,
                voice_id = voiceId,
                model_id = "eleven_multilingual_v2"
            };

            string json = System.Text.Json.JsonSerializer.Serialize(requestBody);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            string jobId = string.Empty;
            try
            {
                string submitUrl = $"https://api.ai84.pro/v2/text-to-speech/async?voice_id={Uri.EscapeDataString(voiceId)}";
                LogTask(task, $"[STEP 4] Creating TTS Job: POST {submitUrl}");
                var response = await httpClient.PostAsync(submitUrl, content);

                string responseContent = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"TTS Job creation failed with status code {response.StatusCode}. Details: {responseContent}");
                }

                using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
                jobId = doc.RootElement.GetProperty("job_id").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(jobId))
                {
                    throw new Exception("Did not receive a valid job_id from API.");
                }

                LogTask(task, $"[STEP 4] Job created successfully. Job ID: {jobId}");
            }
            catch (Exception ex)
            {
                LogTask(task, $"[ERROR] Failed to create TTS job: {ex.Message}");
                throw;
            }

            // Polling Loop
            string audioUrl = string.Empty;
            int attempt = 0;
            while (attempt < 100)
            {
                attempt++;
                await Task.Delay(3000);

                try
                {
                    string statusUrl = $"https://api.ai84.pro/v2/text-to-speech/async/{jobId}";
                    LogTask(task, $"[STEP 4] Polling job status (Attempt {attempt}): GET {statusUrl}");
                    
                    using var statusRequest = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                    statusRequest.Headers.Add("xi-api-key", apiKey);
                    var statusResponse = await httpClient.SendAsync(statusRequest);
                    
                    string statusResponseContent = await statusResponse.Content.ReadAsStringAsync();
                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        LogTask(task, $"[STEP 4] [WARNING] Polling failed: {statusResponseContent}. Retrying...");
                        continue;
                    }

                    using var doc = System.Text.Json.JsonDocument.Parse(statusResponseContent);
                    var jobElement = doc.RootElement.GetProperty("job");
                    string status = jobElement.GetProperty("status").GetString() ?? "queued";

                    LogTask(task, $"[STEP 4] Job status: {status}");

                    if (status.Equals("done", StringComparison.OrdinalIgnoreCase))
                    {
                        LogTask(task, $"[STEP 4] Job completed! Response JSON: {statusResponseContent}");
                        if (jobElement.TryGetProperty("audioUrl", out var audioUrlProp))
                        {
                            audioUrl = audioUrlProp.GetString() ?? string.Empty;
                        }
                        else if (jobElement.TryGetProperty("result", out var resultProp))
                        {
                            audioUrl = resultProp.GetString() ?? string.Empty;
                        }

                        if (string.IsNullOrEmpty(audioUrl))
                        {
                            throw new Exception($"TTS job marked as done but no audio URL key ('audioUrl' or 'result') found. Response JSON: {statusResponseContent}");
                        }
                        break;
                    }
                    else if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
                    {
                        string errMsg = "Unknown error";
                        if (jobElement.TryGetProperty("error_message", out var errProp))
                        {
                            errMsg = errProp.GetString() ?? "Unknown error";
                        }
                        throw new Exception($"TTS Job failed: {errMsg}");
                    }
                }
                catch (Exception ex)
                {
                    LogTask(task, $"[ERROR] Polling status failed: {ex.Message}");
                    throw;
                }
            }

            if (string.IsNullOrEmpty(audioUrl))
            {
                throw new Exception("Timed out waiting for TTS job to complete or audio URL was empty.");
            }

            // Download final audio file
            try
            {
                LogTask(task, $"[STEP 4] Downloading generated voiceover from CDN: {audioUrl}");
                var audioResponse = await httpClient.GetAsync(audioUrl);
                if (!audioResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to download audio from CDN. Status: {audioResponse.StatusCode}");
                }

                string downloadPath = Path.Combine(outputDir, "voiceover.mp3");
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await audioResponse.Content.CopyToAsync(fileStream);
                }

                LogTask(task, $"[STEP 4] Success! Saved voiceover file to: {downloadPath}");
            }
            catch (Exception ex)
            {
                LogTask(task, $"[ERROR] Failed to download audio file: {ex.Message}");
                throw;
            }
        }


        private async Task RunStep5Async(AutomationTask task)
        {
            LogTask(task, "[STEP 5] Starting Image Generation via Image Edits API...");

            string thumbnailPath = Path.Combine(task.OutputDir, $"{task.VideoId}_thumbnail.jpg");
            if (!File.Exists(thumbnailPath))
            {
                throw new FileNotFoundException($"[STEP 5] [ERROR] Thumbnail file not found: {thumbnailPath}. Please run Step 1 first.");
            }

            string apiUrl = "";
            string apiKey = "";
            Dispatcher.Invoke(() =>
            {
                apiUrl = TxtImageApiUrl.Text.Trim();
                apiKey = TxtImageApiKey.Text.Trim();
            });

            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                apiUrl = "http://localhost:8000/v1/images/edits";
            }
            else
            {
                if (!apiUrl.EndsWith("/images/edits"))
                {
                    apiUrl = apiUrl.TrimEnd('/');
                    if (apiUrl.EndsWith("/v1"))
                    {
                        apiUrl += "/images/edits";
                    }
                    else
                    {
                        apiUrl += "/v1/images/edits";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = "chatgpt2api";
            }

            // Run API requests sequentially: Image 1 first, then Image 2
            // Image 1: Translation
            string prompt1 = $"tạo một bức ảnh tương tự với phần văn bản được dịch sang ngôn ngữ '{task.TargetLanguage}', kích thước ảnh 16:9";
            string savePath1 = Path.Combine(task.OutputDir, "translated_thumbnail.png");
            LogTask(task, "[STEP 5] Sending Image 1 (Translation) request to API...");
            try
            {
                await EditImageViaApiAsync(apiUrl, apiKey, thumbnailPath, prompt1, savePath1, task);
                LogTask(task, $"[STEP 5] Success! Saved translated thumbnail to: {savePath1}");
            }
            catch (Exception ex)
            {
                LogTask(task, $"[STEP 5] [ERROR] Failed to generate Image 1: {ex.Message}");
            }

            // Delay for 30 seconds to allow local server / API to cooldown
            LogTask(task, "[STEP 5] Waiting 30 seconds before sending Image 2 request to prevent API overload...");
            await Task.Delay(30000);

            // Image 2: Clean/Remove elements
            string prompt2 = "tạo một bức ảnh tương tự với phần văn bản, biểu tượng mũi tên, vòng tròn (nếu có) được xóa, kích thước ảnh 16:9";
            string savePath2 = Path.Combine(task.OutputDir, "cleaned_thumbnail.png");
            LogTask(task, "[STEP 5] Sending Image 2 (Clean/Remove elements) request to API...");
            try
            {
                await EditImageViaApiAsync(apiUrl, apiKey, thumbnailPath, prompt2, savePath2, task);
                LogTask(task, $"[STEP 5] Success! Saved cleaned thumbnail to: {savePath2}");
            }
            catch (Exception ex)
            {
                LogTask(task, $"[STEP 5] [ERROR] Failed to generate Image 2: {ex.Message}");
            }
        }

        private async Task EditImageViaApiAsync(string apiUrl, string apiKey, string imagePath, string prompt, string savePath, AutomationTask task)
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent("gpt-image-2"), "model");
            content.Add(new StringContent(prompt), "prompt");
            content.Add(new StringContent("1"), "n");

            byte[] fileBytes = await File.ReadAllBytesAsync(imagePath);
            var imageContent = new ByteArrayContent(fileBytes);
            string contentType = imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            imageContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
            content.Add(imageContent, "image", Path.GetFileName(imagePath));

            var response = await httpClient.PostAsync(apiUrl, content);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"API status code {response.StatusCode}. Details: {responseContent}");
            }

            using var doc = System.Text.Json.JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == System.Text.Json.JsonValueKind.Array && dataArray.GetArrayLength() > 0)
            {
                var firstItem = dataArray[0];
                string? imgUrl = null;
                if (firstItem.TryGetProperty("url", out var urlProp))
                {
                    imgUrl = urlProp.GetString();
                }
                else if (firstItem.TryGetProperty("b64_json", out var b64Prop))
                {
                    string b64 = b64Prop.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(b64))
                    {
                        byte[] imgBytes = Convert.FromBase64String(b64);
                        await File.WriteAllBytesAsync(savePath, imgBytes);
                        return;
                    }
                }

                if (!string.IsNullOrEmpty(imgUrl))
                {
                    if (imgUrl.StartsWith("/"))
                    {
                        var uri = new Uri(apiUrl);
                        imgUrl = $"{uri.Scheme}://{uri.Authority}{imgUrl}";
                    }
                    var imgData = await httpClient.GetByteArrayAsync(imgUrl);
                    await File.WriteAllBytesAsync(savePath, imgData);
                }
                else
                {
                    throw new Exception("API response contains no image URL or Base64 data.");
                }
            }
            else
            {
                throw new Exception("No image data returned in API response.");
            }
        }

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

        private async Task UploadImageToChatGPTAsync(IPage page, string base64Data, string fileName)
        {
            await page.EvaluateAsync(@"(args) => {
                const base64Data = args.base64;
                const fileName = args.name;
                const raw = atob(base64Data);
                const rawLength = raw.length;
                const array = new Uint8Array(new ArrayBuffer(rawLength));
                for(let i = 0; i < rawLength; i++) {
                    array[i] = raw.charCodeAt(i);
                }
                const file = new File([array], fileName, { type: 'image/jpeg' });
                const dataTransfer = new DataTransfer();
                dataTransfer.items.add(file);

                const target = document.querySelector('#prompt-textarea') || document.body;
                target.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('dragover', { bubbles: true, cancelable: true, dataTransfer }));
                target.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer }));

                target.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));
                document.body.dispatchEvent(new DragEvent('dragleave', { bubbles: true, cancelable: true, dataTransfer }));

                const cleanOverlay = () => {
                    const allElements = document.querySelectorAll('*');
                    for (const el of allElements) {
                        const style = window.getComputedStyle(el);
                        if ((style.position === 'fixed' || style.position === 'absolute') && 
                            (el.textContent && (el.textContent.includes('Add anything') || el.textContent.includes('Drop any file')))) {
                            el.remove();
                        }
                    }
                };
                cleanOverlay();
                setTimeout(cleanOverlay, 500);
                setTimeout(cleanOverlay, 1500);
            }", new { base64 = base64Data, name = fileName });
        }

        private async Task SendPromptToChatGPTAsync(IPage page, string promptText)
        {
            var promptBox = page.Locator("div#prompt-textarea, div[contenteditable='true']").First;
            await promptBox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await HumanBehaviourHelper.RandomMouseMovementAsync(page);
            await Task.Delay(1000);
            await HumanBehaviourHelper.TypeLikeHumanAsync(page, promptBox, promptText);
            await Task.Delay(1500);
            var sendButton = page.Locator("button[data-testid='send-button'], button[aria-label='Send prompt']").First;
            await sendButton.ClickAsync();
            await Task.Delay(5000);
        }

        private async Task WaitForGenerationToFinishAsync(IPage page)
        {
            int timeoutMs = 240000;
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                var isGenerating = await page.Locator("button[data-testid='stop-button'], button[aria-label='Stop answering']").CountAsync() > 0;
                if (!isGenerating)
                {
                    break;
                }
                await Task.Delay(2000);
                elapsed += 2000;
            }
            await Task.Delay(5000);
        }

        private async Task<string?> ExtractGeneratedImageUrlAsync(IPage page)
        {
            return await page.EvaluateAsync<string?>(@"() => {
                const articles = document.querySelectorAll('article');
                if (articles.length === 0) return null;
                const lastArticle = articles[articles.length - 1];
                const imgs = Array.from(lastArticle.querySelectorAll('img'));
                const dalleImg = imgs.find(img => img.src.includes('backend-api/estuary/content') || img.src.includes('oaiusercontent') || img.src.includes('dalle') || (img.naturalWidth > 200 || img.width > 200));
                return dalleImg ? dalleImg.src : null;
            }");
        }

        private async Task DownloadImageFromPageAsync(IPage page, string src, string savePath)
        {
            string base64Data = await page.EvaluateAsync<string>(@"async (imgSrc) => {
                const res = await fetch(imgSrc);
                const blob = await res.blob();
                return new Promise((resolve) => {
                    const reader = new FileReader();
                    reader.onloadend = () => resolve(reader.result.split(',')[1]);
                    reader.readAsDataURL(blob);
                });
            }", src);

            byte[] bytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(savePath, bytes);
        }
    }

    public static class HumanBehaviourHelper
    {
        public static async Task TypeLikeHumanAsync(IPage page, ILocator locator, string text)
        {
            await locator.FocusAsync();
            var random = new Random();
            foreach (var ch in text)
            {
                await page.Keyboard.TypeAsync(ch.ToString());
                await Task.Delay(random.Next(20, 70));
            }
        }

        public static async Task RandomMouseMovementAsync(IPage page)
        {
            var random = new Random();
            int steps = random.Next(3, 8);
            for (int i = 0; i < steps; i++)
            {
                int x = random.Next(100, 700);
                int y = random.Next(100, 500);
                await page.Mouse.MoveAsync(x, y, new MouseMoveOptions { Steps = random.Next(2, 5) });
                await Task.Delay(random.Next(50, 150));
            }
        }
    }
}
