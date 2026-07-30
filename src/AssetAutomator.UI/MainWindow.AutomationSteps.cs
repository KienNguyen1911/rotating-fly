using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Playwright;
using AssetAutomator.Core.Models;

namespace AssetAutomator.UI
{
    /// <summary>
    /// Orchestrates the automation pipeline by delegating to individual step services and LegacyVideoPipelineService.
    /// </summary>
    public partial class MainWindow : Window
    {
        private async Task RunSingleVideoFlowAsync(AutomationTask task)
        {
            await _legacyVideoPipelineService.ExecutePipelineAsync(
                task: task,
                browserContext: null,
                chatGptPage: null,
                logTask: LogTask
            );
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
