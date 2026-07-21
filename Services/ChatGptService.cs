using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator
{
    /// <summary>
    /// Encapsulates ChatGPT browser interaction logic: drag-drop file upload, prompt submission,
    /// generation waiting, and response extraction.
    /// Extracted from MainWindow.AutomationSteps.cs to eliminate duplication and follow SRP.
    /// </summary>
    public class ChatGptService
    {
        /// <summary>
        /// Shared JS code for simulating file drag-and-drop onto the ChatGPT prompt area.
        /// Consolidates duplicate JS from RunStep3Async and UploadImageToChatGPTAsync.
        /// </summary>
        private const string DragDropJsTemplate = @"(args) => {
            const base64Data = args.base64;
            const fileName = args.name;
            const mimeType = args.mime || 'text/plain';
            const raw = atob(base64Data);
            const rawLength = raw.length;
            const array = new Uint8Array(new ArrayBuffer(rawLength));
            for(let i = 0; i < rawLength; i++) {
                array[i] = raw.charCodeAt(i);
            }
            const file = new File([array], fileName, { type: mimeType });
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
        }";

        /// <summary>
        /// Simulates a file drag-and-drop onto the ChatGPT page.
        /// Works for both text files and images.
        /// </summary>
        public async Task SimulateDragDropFileAsync(IPage page, string base64Data, string fileName, string mimeType = "text/plain")
        {
            await page.EvaluateAsync(DragDropJsTemplate, new { base64 = base64Data, name = fileName, mime = mimeType });
        }

        /// <summary>
        /// Types a prompt into ChatGPT's input box using human-like typing and submits it.
        /// </summary>
        public async Task SendPromptAsync(IPage page, string promptText)
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

        /// <summary>
        /// Waits for ChatGPT to finish generating a response by polling for the stop button.
        /// </summary>
        public async Task WaitForGenerationToFinishAsync(IPage page, int timeoutMs = 1800000)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                var isGenerating = await page.Locator("button[data-testid='stop-button'], button[aria-label='Stop generating'], button[aria-label='Stop answering']").CountAsync() > 0;
                if (!isGenerating)
                {
                    break;
                }
                await Task.Delay(5000);
                elapsed += 5000;
            }
            await Task.Delay(2000);
        }

        /// <summary>
        /// Extracts the text content of the last ChatGPT response from the page.
        /// </summary>
        public async Task<string?> ExtractLastResponseTextAsync(IPage page)
        {
            var articles = page.Locator("article");
            var count = await articles.CountAsync();
            if (count > 0)
            {
                var lastArticle = articles.Nth(count - 1);
                var contentLocator = lastArticle.Locator(".markdown, div[class*='content']").First;
                return await contentLocator.InnerTextAsync();
            }
            else
            {
                var responses = page.Locator("div.agent-turn");
                var resCount = await responses.CountAsync();
                if (resCount > 0)
                {
                    return await responses.Nth(resCount - 1).InnerTextAsync();
                }
            }
            return null;
        }

        /// <summary>
        /// Extracts the URL of a generated image from the last ChatGPT response.
        /// </summary>
        public async Task<string?> ExtractGeneratedImageUrlAsync(IPage page)
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

        /// <summary>
        /// Downloads an image from the browser page context and saves it locally.
        /// </summary>
        public async Task DownloadImageFromPageAsync(IPage page, string src, string savePath)
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
            await System.IO.File.WriteAllBytesAsync(savePath, bytes);
        }
    }
}
