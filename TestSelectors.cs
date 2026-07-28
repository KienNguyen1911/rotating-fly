using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator
{
    public class SelectorTester
    {
        private readonly string _chromeProfilePath;
        private readonly Action<string> _log;

        public SelectorTester(string chromeProfilePath, Action<string> log)
        {
            _chromeProfilePath = chromeProfilePath;
            _log = log;
        }

        public async Task RunAsync()
        {
            _log("🔍 Testing selectors on Gemini...");

            IPlaywright? playwright = null;
            IBrowserContext? context = null;
            IPage? page = null;

            try
            {
                playwright = await Playwright.CreateAsync();

                context = await playwright.Chromium.LaunchPersistentContextAsync(
                    _chromeProfilePath,
                    new BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = false,
                        Channel = "chrome",
                        Args = new[]
                        {
                            "--disable-blink-features=AutomationControlled",
                            "--no-sandbox",
                            "--start-maximized"
                        },
                        ViewportSize = new ViewportSize { Width = 1400, Height = 900 }
                    });

                page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();

                await page.GotoAsync("https://gemini.google.com/app", new PageGotoOptions 
                { 
                    WaitUntil = WaitUntilState.NetworkIdle, 
                    Timeout = 60000 
                });

                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15000 });
                await Task.Delay(3000);

                // Test each selector
                await TestSelector(page, "[data-test-id='textarea-inner']", "Textarea inner");
                await TestSelector(page, "[data-test-id='bard-mode-menu-button']", "Model menu button");
                await TestSelector(page, "[data-test-id='gem-mode-menu']", "Gem mode menu");
                await TestSelector(page, "[aria-label='Nhập câu lệnh cho Gemini']", "Input area (VN)");
                await TestSelector(page, "[aria-label*='Ask Gemini']", "Input area (EN)");
                await TestSelector(page, "[data-test-id='send-button']", "Send button");
                await TestSelector(page, "button[aria-label*='Gửi']", "Send button (VN)");
                await TestSelector(page, "rich-textarea", "Rich textarea");
                await TestSelector(page, "rich-textarea div.ql-editor[contenteditable='true']", "QL Editor");
                await TestSelector(page, "[data-test-id='gem-mode-menu'] [role='menuitem']", "Model menu items");
                await TestSelector(page, "button:has-text('Hỏi Gemini')", "New chat trigger (VN)");
                await TestSelector(page, "button:has-text('Ask Gemini')", "New chat trigger (EN)");

                _log("\n✅ Selector test complete. Check results above.");
                _log("Press Enter to close browser...");
                Console.ReadLine();
            }
            catch (Exception ex)
            {
                _log($"❌ Error: {ex.Message}");
            }
            finally
            {
                if (context != null) await context.CloseAsync();
                playwright?.Dispose();
            }
        }

        private async Task TestSelector(IPage page, string selector, string description)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    var isVisible = await element.IsVisibleAsync();
                    var tagName = await element.EvaluateAsync<string>("el => el.tagName");
                    var className = await element.GetAttributeAsync("class");
                    var ariaLabel = await element.GetAttributeAsync("aria-label");
                    var testId = await element.GetAttributeAsync("data-test-id");

                    _log($"✅ [{description}] FOUND");
                    _log($"   Selector: {selector}");
                    _log($"   Tag: {tagName}, Visible: {isVisible}");
                    _log($"   Class: {className ?? "none"}");
                    _log($"   Aria-label: {ariaLabel ?? "none"}");
                    _log($"   Data-test-id: {testId ?? "none"}");
                }
                else
                {
                    _log($"❌ [{description}] NOT FOUND");
                    _log($"   Selector: {selector}");
                }
            }
            catch (Exception ex)
            {
                _log($"⚠️ [{description}] ERROR: {ex.Message}");
                _log($"   Selector: {selector}");
            }
            _log(""); // blank line
        }
    }
}