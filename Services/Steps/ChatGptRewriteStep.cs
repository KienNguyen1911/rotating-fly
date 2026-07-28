using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Services;

namespace AssetAutomator
{
    /// <summary>
    /// Step 3: Rewrites the transcript using ChatGPT via browser automation.
    /// Uses ChatGptService for drag-drop, prompt submission, and response extraction.
    /// </summary>
    public class ChatGptRewriteStep
    {
        private readonly ChatGptService _chatGptService;
        private readonly IConfigService _configService;

        public ChatGptRewriteStep(ChatGptService chatGptService, IConfigService configService)
        {
            _chatGptService = chatGptService;
            _configService = configService;
        }

        public async Task<string?> ExecuteAsync(
            string targetLanguage,
            string outputDir,
            string transcriptText,
            string videoId,
            AutomationTask task,
            IBrowserContext context,
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 3] Starting ChatGPT rewrite...");
            if (context == null) throw new InvalidOperationException("Browser not initialized.");

            var page = await context.NewPageAsync();
            string? scriptText = null;
            try
            {
                string? customGptUrl = null;
                if (!string.IsNullOrEmpty(task.SelectedProfile))
                {
                    string baseDir = _configService.CurrentSettings.ChromeProfilesDir;
                    if (string.IsNullOrEmpty(baseDir))
                    {
                        baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
                    }
                    string profilePath = Path.Combine(baseDir, task.SelectedProfile);
                    string gptUrlPath = Path.Combine(profilePath, "gpt_url.txt");
                    if (File.Exists(gptUrlPath))
                    {
                        customGptUrl = File.ReadAllText(gptUrlPath).Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(customGptUrl))
                {
                    customGptUrl = _configService.CurrentSettings.CustomGptUrl;
                    if (string.IsNullOrWhiteSpace(customGptUrl))
                    {
                        customGptUrl = "https://chatgpt.com/g/g-6a4083a0e37081919a248ef7721dae3d-dich-chay";
                    }
                }
                logTask(task, $"Navigating to Custom GPT URL: {customGptUrl} ...");
                await page.GotoAsync(customGptUrl);
                await Task.Delay(4000);

                logTask(task, "Preparing transcript file for drag & drop...");
                string transcriptPath = Path.Combine(outputDir, "transcript.txt");
                if (!File.Exists(transcriptPath))
                {
                    await File.WriteAllTextAsync(transcriptPath, transcriptText);
                }

                await Task.Delay(4000);

                // Read file as Base64 and use ChatGptService for drag-drop
                byte[] fileBytes = await File.ReadAllBytesAsync(transcriptPath);
                string base64File = Convert.ToBase64String(fileBytes);

                await Task.Delay(4000);

                logTask(task, "Simulating drag & drop of transcript.txt onto ChatGPT page...");
                await _chatGptService.SimulateDragDropFileAsync(page, base64File, "transcript.txt", "text/plain");

                logTask(task, "Drag & Drop simulated, waiting 4 seconds for upload processing...");
                await Task.Delay(4000);

                logTask(task, "Finding prompt input box to enter instruction...");
                var promptBox = page.Locator("div#prompt-textarea, div[contenteditable='true']").First;
                await promptBox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                await HumanBehaviourHelper.RandomMouseMovementAsync(page);
                await Task.Delay(1500);

                string promptText = $"Hãy viết lại kịch bản sau đây bằng ngôn ngữ '{targetLanguage}' , chỉ dịch, không chỉnh sửa nội dung sẵn có trong script";
                logTask(task, "Typing prompt using Human Typing simulator...");
                await HumanBehaviourHelper.TypeLikeHumanAsync(page, promptBox, promptText);
                await Task.Delay(2000);

                logTask(task, "Submitting prompt...");
                var sendButton = page.Locator("button[data-testid='send-button'], button[aria-label='Send prompt']").First;
                await sendButton.ClickAsync();
                await Task.Delay(5000);

                logTask(task, "Waiting for AI to finish writing script...");
                await _chatGptService.WaitForGenerationToFinishAsync(page, 1800000);

                await Task.Delay(2000);

                logTask(task, "Extracting the ChatGPT response...");
                scriptText = await _chatGptService.ExtractLastResponseTextAsync(page);

                if (!string.IsNullOrWhiteSpace(scriptText))
                {
                    logTask(task, $"Success! Generated script length: {scriptText.Length} characters.");
                    string outputPath = Path.Combine(outputDir, "rewritten_script.txt");
                    await File.WriteAllTextAsync(outputPath, scriptText);
                    logTask(task, $"Saved rewritten script to: {outputPath}");
                }
                else
                {
                    logTask(task, "[ERROR] Failed to extract rewritten script content.");
                }
            }
            catch (Exception ex)
            {
                logTask(task, $"[ERROR] ChatGPT execution failed: {ex.Message}");
                throw;
            }
            finally
            {
                await page.CloseAsync();
            }

            return scriptText;
        }
    }
}
