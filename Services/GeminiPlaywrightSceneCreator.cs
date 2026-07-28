using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Services;

namespace AssetAutomator
{
    /// <summary>
    /// Uses Playwright to automate Gemini Web UI for Scene Creator operations.
    /// Benefits over API: native thinking mode, better file handling, no 502 errors.
    /// Requires a Chrome profile with an active Gemini session.
    /// </summary>
    public class GeminiPlaywrightSceneCreator
    {
        private readonly string _chromeProfilePath;
        private readonly Action<string> _log;
        private IPlaywright? _playwright;
        private IBrowserContext? _context;
        private IPage? _page;

        /// <summary>Gemini base URL (may vary by region).</summary>
        private const string GEMINI_URL = "https://gemini.google.com/app";

        /// <summary>Timeout for waiting on selectors (ms).</summary>
        private const int SELECTOR_TIMEOUT = 30000;

        public GeminiPlaywrightSceneCreator(string chromeProfilePath, Action<string> log)
        {
            _chromeProfilePath = chromeProfilePath;
            _log = log;
        }

        /// <summary>
        /// Generates scene breakdown via Gemini Web UI using Playwright.
        /// Returns (sceneJsonText, thoughtsText).
        /// </summary>
        public async Task<(string SceneJson, string? Thoughts)> CreateScenesAsync(
            string outputDir,
            string srtPath,
            string transcriptPath,
            string gemName,
            string? gemId,
            string modelDisplayName,
            bool enableExtendedThinking)
        {
            _log("[PW-SCENE] 🌐 Launching Playwright browser for Gemini Scene Creator...");

            try
            {
                await InitializeBrowserAsync();

                // Navigate to Gemini — go directly to gem page if ID available
                string targetUrl = !string.IsNullOrWhiteSpace(gemId)
                    ? $"https://gemini.google.com/gem/{gemId}"
                    : GEMINI_URL;
                _log($"[PW-SCENE] Navigating to: {targetUrl}");
                _log($"[PW-SCENE] ⏳ Calling GotoAsync with WaitUntil=DOMContentLoaded (30s timeout)...");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // Use DOMContentLoaded instead of NetworkIdle — Gemini is a heavy SPA
                    // with persistent WebSocket/background fetches, so NetworkIdle is never
                    // reached and the call hangs until timeout.
                    await _page!.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
                    sw.Stop();
                    _log($"[PW-SCENE] ✅ GotoAsync completed in {sw.ElapsedMilliseconds}ms. Current URL: {_page.Url}");
                }
                catch (Exception navEx)
                {
                    sw.Stop();
                    _log($"[PW-SCENE] ❌ GotoAsync failed after {sw.ElapsedMilliseconds}ms: {navEx.GetType().Name}: {navEx.Message}");
                    _log($"[PW-SCENE] 📌 Current URL when exception: {_page.Url}");
                    throw;
                }

                // Wait for Gemini to fully load (input area, etc.)
                await WaitForGeminiReadyAsync();

                // Select the Gem if specified (only needed on main page, not gem page)
                if (!string.IsNullOrWhiteSpace(gemName) && gemName != "-- Gemini Mặc Định --")
                {
                    await SelectGemAsync(gemName);
                }

                // Select model and thinking mode
                await SelectModelAsync(modelDisplayName, enableExtendedThinking);

                // Build and send prompt with file attachments
                string prompt = BuildScenePrompt();
                _log($"[PW-SCENE] 📝 Sending prompt ({prompt.Length} chars) with {GetAttachedFileCount(srtPath, transcriptPath)} files...");

                var (responseText, thoughts) = await SendPromptThenWaitWithRetryAsync(
                    prompt, srtPath, transcriptPath, outputDir);

                _log($"[PW-SCENE] ✅ Response received: text={responseText.Length} chars, thoughts={thoughts?.Length ?? 0} chars");

                // Save thoughts
                if (!string.IsNullOrWhiteSpace(thoughts))
                {
                    string thoughtsPath = Path.Combine(outputDir, "scene_thoughts.txt");
                    await File.WriteAllTextAsync(thoughtsPath, thoughts, System.Text.Encoding.UTF8);
                    _log($"[PW-SCENE] 🧠 Thinking process saved ({thoughts.Length} chars) → scene_thoughts.txt");
                }
                else
                {
                    _log("[PW-SCENE] ⚠️ No thinking/thoughts captured from Web UI.");
                }

                return (responseText, thoughts);
            }
            catch (Exception ex)
            {
                // Take a screenshot for debugging
                try
                {
                    string ssPath = Path.Combine(outputDir, "pw_scene_error.png");
                    await _page!.ScreenshotAsync(new PageScreenshotOptions { Path = ssPath });
                    _log($"[PW-SCENE] 📸 Error screenshot saved: {ssPath}");
                }
                catch { }
                _log($"[PW-SCENE] ❌ Fatal error: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                await CleanupAsync();
            }
        }

        /// <summary>
        /// Sends the prompt and waits for the response.
        /// If the wait times out (Gemini still thinking after 10 min),
        /// captures the chat URL, closes the browser, waits 20s, reopens the chat URL,
        /// sends "Continue", and waits again. Repeats up to maxContinueAttempts times.
        /// </summary>
        private async Task<(string ResponseText, string? Thoughts)> SendPromptThenWaitWithRetryAsync(
            string prompt, string srtPath, string transcriptPath, string outputDir)
        {
            const int MAX_CONTINUE_ATTEMPTS = 3;
            const int COOLDOWN_SECONDS = 20;

            // First attempt: send prompt with files
            var result = await SendPromptWithFilesAsync(prompt, srtPath, transcriptPath);

            // If we got a real response on the first try, return it
            if (!string.IsNullOrWhiteSpace(result.ResponseText) && result.ResponseText.Length >= RESPONSE_MIN_CHARS)
            {
                _log($"[PW-SCENE] 🎉 Got response on first try: {result.ResponseText.Length} chars.");
                return (result.ResponseText, result.Thoughts);
            }

            // Wait for response with retry on timeout
            for (int attempt = 1; attempt <= MAX_CONTINUE_ATTEMPTS; attempt++)
            {
                try
                {
                    var waited = await WaitForResponseAsync();
                    return waited;
                }
                catch (TimeoutException tex)
                {
                    _log($"[PW-SCENE] ⏰ Timeout on attempt {attempt}/{MAX_CONTINUE_ATTEMPTS}: {tex.Message}");

                    if (attempt >= MAX_CONTINUE_ATTEMPTS)
                    {
                        _log($"[PW-SCENE] ❌ Max continue attempts reached. Giving up.");
                        throw;
                    }

                    // Capture chat URL before closing browser
                    string? chatUrl = await GetCurrentChatUrlAsync();
                    if (string.IsNullOrWhiteSpace(chatUrl))
                    {
                        _log($"[PW-SCENE] ⚠️ Could not capture chat URL. Skipping continue retry.");
                        throw;
                    }

                    // Save URL to file for debugging
                    try
                    {
                        string urlPath = Path.Combine(outputDir, "gemini_chat_url.txt");
                        await File.WriteAllTextAsync(urlPath, chatUrl, System.Text.Encoding.UTF8);
                        _log($"[PW-SCENE] 💾 Chat URL saved → {urlPath}");
                    }
                    catch { }

                    // Close browser cleanly
                    await CleanupAsync();

                    // Cooldown before reopening
                    _log($"[PW-SCENE] 😴 Waiting {COOLDOWN_SECONDS}s before reopening browser...");
                    await Task.Delay(TimeSpan.FromSeconds(COOLDOWN_SECONDS));

                    // Reopen browser + navigate to chat URL
                    _log($"[PW-SCENE] 🔄 Reopening browser at: {chatUrl}");
                    await InitializeBrowserAsync();
                    await _page!.GotoAsync(chatUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 });
                    await WaitForGeminiReadyAsync();

                    // Wait a bit for the chat history to load
                    await Task.Delay(3000);

                    // Send "Continue" to resume generation
                    bool resumed = await SendContinueMessageAsync();
                    if (!resumed)
                    {
                        _log($"[PW-SCENE] ❌ Failed to send 'Continue'. Giving up.");
                        throw new TimeoutException("Failed to send 'Continue' after timeout.");
                    }

                    _log($"[PW-SCENE] 🔁 Resumed generation. Continuing wait (attempt {attempt + 1}/{MAX_CONTINUE_ATTEMPTS})...");
                }
            }

            // Should never reach here
            throw new TimeoutException("SendPromptThenWaitWithRetryAsync exited loop without returning.");
        }

        private async Task InitializeBrowserAsync()
        {
            _log($"[PW-SCENE] 🔧 Creating Playwright instance...");
            _playwright = await Playwright.CreateAsync();
            _log($"[PW-SCENE] 🔧 Launching Chromium with persistent profile at: {_chromeProfilePath}");
            _log($"[PW-SCENE] 🔧 Profile directory exists: {Directory.Exists(_chromeProfilePath)}");

            // Use persistent context to reuse Chrome profile with existing Gemini session
            _context = await _playwright.Chromium.LaunchPersistentContextAsync(
                _chromeProfilePath,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = "chrome",
                    Args = new[]
                    {
                        "--disable-blink-features=AutomationControlled",
                        "--no-sandbox",
                        "--disable-features=TranslateUI",
                        "--start-maximized"
                    },
                    ViewportSize = new ViewportSize { Width = 1400, Height = 900 }
                });
            _log($"[PW-SCENE] ✅ Browser launched. Pages in context: {_context.Pages.Count}");

            _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
            _log($"[PW-SCENE] ✅ Page acquired. Current URL: {_page.Url}");
        }

        private async Task WaitForGeminiReadyAsync()
        {
            _log("[PW-SCENE] ⏳ Waiting for Gemini page to load...");

            // Gem pages (gemini.google.com/gem/...) have a different layout than the main app.
            // The input area may not be visible until a new chat is started.
            // Strategy: wait for the page body, then click the "new chat" trigger if needed.

            // Step 1: Wait for page to finish loading
            try
            {
                _log("[PW-SCENE] ⏳ Step 1: Waiting for NetworkIdle (15s timeout)...");
                await _page!.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 15000 });
                _log("[PW-SCENE] ✅ Network reached idle state.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ NetworkIdle wait failed: {ex.GetType().Name}: {ex.Message}");
            }
            await Task.Delay(2000);

            // Step 2: Check if input area is already visible (main Gemini page)
            try
            {
                _log("[PW-SCENE] 🔎 Step 2: Querying existing input area [data-test-id='textarea-inner']...");
                var existingInput = await _page!.QuerySelectorAsync("[data-test-id='textarea-inner']");
                if (existingInput != null)
                {
                    _log("[PW-SCENE] ✅ Input area already visible.");
                    await Task.Delay(500);
                    return;
                }
                _log("[PW-SCENE] ⚠️ Input area not found on first query. Will try 'new chat' trigger...");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Query input area error: {ex.GetType().Name}: {ex.Message}");
            }

            // Step 3: On Gem pages, click "new chat" trigger to show the input
            _log("[PW-SCENE] 🔍 Input not visible — looking for 'new chat' trigger on Gem page...");
            try
            {
                // Gem pages have a "+ Hỏi Gemini" or similar button at the bottom
                var newChatTriggers = new[]
                {
                    "button:has-text('Hỏi Gemini')",       // Vietnamese "+ Hỏi Gemini"
                    "[aria-label*='Hỏi Gemini']",
                    "[aria-label*='Ask Gemini']",
                    "button:has-text('Ask Gemini')",       // English
                    "button[aria-label*='New chat']",
                    "[data-test-id='new-chat-button']",
                    // Fallback: any prominent button near the bottom
                    "button:has-text('Gemini')"
                };

                foreach (var sel in newChatTriggers)
                {
                    try
                    {
                        _log($"[PW-SCENE] 🔎 Trying new chat trigger: '{sel}'");
                        var btn = await _page!.WaitForSelectorAsync(sel, new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                        if (btn != null)
                        {
                            await btn.ClickAsync();
                            _log($"[PW-SCENE] ✅ Clicked new chat trigger: '{sel}'");
                            await Task.Delay(1500);
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ New chat trigger '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                _log("[PW-SCENE] ⚠️ All new chat trigger selectors exhausted. Input area may never appear.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ❌ New chat trigger block failed: {ex.GetType().Name}: {ex.Message}");
            }

            // Step 4: Now wait for the input area to appear
            try
            {
                _log("[PW-SCENE] 🔎 Trying primary input selector: [data-test-id='textarea-inner']");
                await _page!.WaitForSelectorAsync(
                    "[data-test-id='textarea-inner']",
                    new PageWaitForSelectorOptions { Timeout = 10000, State = WaitForSelectorState.Visible });
                _log("[PW-SCENE] ✅ Input area appeared after clicking new chat.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Primary input selector failed: {ex.GetType().Name}: {ex.Message}");
                // Fallback: try aria-label
                try
                {
                    _log("[PW-SCENE] 🔎 Trying fallback input selector: [aria-label='Nhập câu lệnh cho Gemini'], rich-textarea");
                    await _page!.WaitForSelectorAsync(
                        "[aria-label='Nhập câu lệnh cho Gemini'], rich-textarea",
                        new PageWaitForSelectorOptions { Timeout = 5000, State = WaitForSelectorState.Visible });
                    _log("[PW-SCENE] ✅ Fallback input selector matched.");
                }
                catch (Exception ex2)
                {
                    _log($"[PW-SCENE] ❌ Fallback input selector failed: {ex2.GetType().Name}: {ex2.Message}");
                    _log("[PW-SCENE] ⚠️ Input area still not visible. Continuing anyway...");
                    // Take a screenshot for debugging
                    try { await _page!.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(Path.GetTempPath(), "gemini_debug.png") }); } catch { }
                }
            }

            await Task.Delay(1000); // Let UI settle
            _log("[PW-SCENE] ✅ Gemini is ready.");
        }

        private async Task SelectGemAsync(string gemName)
        {
            _log($"[PW-SCENE] 🔧 Selecting Gem: '{gemName}'...");
            try
            {
                // Click on the gem selector (usually near the model selector at top)
                // Try multiple selector strategies
                var gemSelectors = new[]
                {
                    "button:has-text('Gem')",
                    "[aria-label*='Gem']",
                    "[data-test-id='gem-selector']",
                    "button:has-text('" + gemName + "')"
                };

                foreach (var sel in gemSelectors)
                {
                    try
                    {
                        _log($"[PW-SCENE] 🔎 Trying gem selector button: '{sel}'");
                        var btn = await _page!.WaitForSelectorAsync(sel, new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                        if (btn != null)
                        {
                            await btn.ClickAsync();
                            await Task.Delay(500);
                            _log($"[PW-SCENE] ✅ Clicked gem selector: '{sel}'");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Gem selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Now find and click the specific gem in the dropdown
                var gemItemSelectors = new[]
                {
                    $"text={gemName}",
                    $"[role='option']:has-text('{gemName}')",
                    $"li:has-text('{gemName}')"
                };

                foreach (var sel in gemItemSelectors)
                {
                    try
                    {
                        _log($"[PW-SCENE] 🔎 Trying gem item: '{sel}'");
                        var item = await _page!.WaitForSelectorAsync(sel, new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                        if (item != null)
                        {
                            await item.ClickAsync();
                            await Task.Delay(800);
                            _log($"[PW-SCENE] ✅ Gem '{gemName}' selected via '{sel}'.");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Gem item '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                _log($"[PW-SCENE] ⚠️ Could not select Gem '{gemName}' via UI. Continuing with default.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Gem selection failed: {ex.Message}. Continuing with default.");
            }
        }

        private async Task SelectModelAsync(string modelDisplayName, bool enableExtendedThinking)
        {
            _log($"[PW-SCENE] 🔧 Selecting model: '{modelDisplayName}', extended thinking={enableExtendedThinking}...");
            try
            {
                // Step 1: Open the model selector dropdown
                // Real DOM: <button data-test-id="bard-mode-menu-button" aria-label="Mở công cụ chọn chế độ, hiện tại là Flash">
                bool opened = false;
                try
                {
                    _log("[PW-SCENE] 🔎 Step 1: Trying [data-test-id='bard-mode-menu-button']...");
                    var btn = await _page!.WaitForSelectorAsync(
                        "[data-test-id='bard-mode-menu-button']",
                        new PageWaitForSelectorOptions { Timeout = 5000, State = WaitForSelectorState.Visible });
                    if (btn != null) { await btn.ClickAsync(); await Task.Delay(800); opened = true; _log("[PW-SCENE] ✅ Opened model selector dropdown."); }
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ bard-mode-menu-button failed: {ex.GetType().Name}: {ex.Message}");
                }

                // Fallback: try aria-label
                if (!opened)
                {
                    try
                    {
                        _log("[PW-SCENE] 🔎 Trying fallback aria-label '[aria-label*='chế độ']'...");
                        var btn = _page!.Locator("[aria-label*='chọn chế độ']").First;
                        await btn.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                        await Task.Delay(800);
                        opened = true;
                        _log("[PW-SCENE] ✅ Opened model selector dropdown via aria-label.");
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ aria-label fallback failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (!opened)
                {
                    _log("[PW-SCENE] ❌ Could not open model selector dropdown. Skipping model selection.");
                    return;
                }

                // Step 2: Wait for the menu to appear (real DOM: [data-test-id="gem-mode-menu"] role="menu")
                try
                {
                    _log("[PW-SCENE] 🔎 Step 2: Waiting for menu [data-test-id='gem-mode-menu']...");
                    await _page!.WaitForSelectorAsync("[data-test-id='gem-mode-menu']", new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                    _log("[PW-SCENE] ✅ Model menu visible.");
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ gem-mode-menu wait failed: {ex.GetType().Name}: {ex.Message}");
                    await Task.Delay(1000); // wait anyway
                }

                // Step 3: Select the model — Gemini DOM uses <gem-menu-item> custom elements
                // with text containing the model name. Try multiple selectors and lookups.
                bool modelSelected = false;
                var modelSelectors = new[]
                {
                    // Primary: gem-menu-item inside gem-mode-menu
                    $"[data-test-id='gem-mode-menu'] gem-menu-item:has-text(\"{modelDisplayName}\")",
                    $"gem-mode-menu gem-menu-item:has-text(\"{modelDisplayName}\")",
                    // Aria-label inside menuitem
                    $"[data-test-id='gem-mode-menu'] [role='menuitem']:has-text(\"{modelDisplayName}\")",
                    $"[data-test-id='gem-mode-menu'] [role='menuitemradio']:has-text(\"{modelDisplayName}\")",
                    // Plain menuitem
                    $"[role='menuitem']:has-text(\"{modelDisplayName}\")",
                    $"[role='menuitemradio']:has-text(\"{modelDisplayName}\")",
                };

                foreach (var sel in modelSelectors)
                {
                    try
                    {
                        _log($"[PW-SCENE] 🔎 Step 3: Trying model selector '{sel}'");
                        var modelItem = _page!.Locator(sel).First;
                        await modelItem.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                        await Task.Delay(500);
                        _log($"[PW-SCENE] ✅ Model '{modelDisplayName}' selected via '{sel}'.");
                        modelSelected = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Model selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (!modelSelected)
                {
                    // Check if model already selected — read aria-label of bard-mode-menu-button
                    try
                    {
                        var currentLabel = await _page!.Locator("[data-test-id='bard-mode-menu-button']").GetAttributeAsync("aria-label");
                        _log($"[PW-SCENE] ⚠️ Model menuitem not found. Current button aria-label: '{currentLabel}'");
                        if (currentLabel != null && currentLabel.Contains(modelDisplayName, StringComparison.OrdinalIgnoreCase))
                        {
                            _log($"[PW-SCENE] ✅ Model '{modelDisplayName}' already selected (matches button label).");
                            modelSelected = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Could not read current model label: {ex.Message}");
                    }

                    if (!modelSelected)
                    {
                        _log($"[PW-SCENE] ⚠️ Could not select model '{modelDisplayName}'. Continuing with current model.");
                    }
                }

                // Step 4: Enable extended thinking if requested
                // Real DOM: <gem-menu-item role="menuitem"> with <span class="label"> Tư duy mở rộng </span>
                // It's in the same [data-test-id="gem-mode-menu"], after a <mat-divider>
                if (enableExtendedThinking)
                {
                    try
                    {
                        _log("[PW-SCENE] 🧠 Looking for 'Tư duy mở rộng' in model menu...");

                        // Re-open the menu if needed (selecting model may have closed it)
                        await Task.Delay(500);

                        // Find the thinking menuitem — try Vietnamese first, then English
                        var thinkingItem = _page.Locator("[data-test-id='gem-mode-menu'] [role='menuitem']:has-text('Tư duy mở rộng')").First;
                        await thinkingItem.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                        await Task.Delay(500);
                        _log("[PW-SCENE] ✅ Extended thinking enabled.");
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Vietnamese thinking menuitem failed: {ex.GetType().Name}: {ex.Message}");
                        // Try English fallback
                        try
                        {
                            var thinkingItem = _page.Locator("[data-test-id='gem-mode-menu'] [role='menuitem']:has-text('Extended thinking')").First;
                            await thinkingItem.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
                            await Task.Delay(500);
                            _log("[PW-SCENE] ✅ Extended thinking enabled (EN).");
                        }
                        catch (Exception ex2)
                        {
                            _log($"[PW-SCENE] ⚠️ English thinking menuitem failed: {ex2.GetType().Name}: {ex2.Message}. Extended thinking option not found in menu.");
                        }
                    }
                }

                _log($"[PW-SCENE] ✅ Model configuration complete.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Model selection failed: {ex.Message}. Using default.");
            }
        }

        private static string BuildScenePrompt()
        {
            return @"Tạo scenes JSON cho video từ file SRT và transcript đính kèm.";
        }

        private static int GetAttachedFileCount(string srtPath, string transcriptPath)
        {
            int count = 0;
            if (File.Exists(srtPath)) count++;
            if (File.Exists(transcriptPath)) count++;
            return count;
        }

        private async Task<(string ResponseText, string? Thoughts)> SendPromptWithFilesAsync(
            string prompt, string srtPath, string transcriptPath)
        {
            // Single attempt — no retry. Gemini takes 2-5 minutes of thinking,
            // and any "retry" while Gemini is still generating will:
            //   1) attach the same files again (over-attaching)
            //   2) type the prompt again (spamming input)
            //   3) click send again (double-sending)
            // ...which causes Gemini to lose context or error out.
            // Caller is expected to wait the full 10-minute max in WaitForResponseAsync.

            try
            {
                await AttachFilesAsync(srtPath, transcriptPath);
                await TypePromptAsync(prompt);
                await ClickSendAsync();
                (string text, string? thoughts) = await WaitForResponseAsync();

                _log($"[PW-SCENE] 📦 Final response: {text.Length} chars.");
                return (text, thoughts);
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ SendPromptWithFilesAsync failed: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private async Task AttachFilesAsync(string srtPath, string transcriptPath)
        {
            try
            {
                var filesToAttach = new List<string>();
                if (File.Exists(transcriptPath)) filesToAttach.Add(transcriptPath);
                if (File.Exists(srtPath)) filesToAttach.Add(srtPath);
                if (filesToAttach.Count == 0)
                {
                    _log("[PW-SCENE] ⚠️ No files to attach (SRT/Transcript not found).");
                    return;
                }

                _log($"[PW-SCENE] 📎 Drag & dropping {filesToAttach.Count} files: {string.Join(", ", filesToAttach.Select(Path.GetFileName))}");

                // Strategy 1: Drag & drop files onto the page (most reliable for Gemini)
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 1: Try SetInputFiles on common drop targets...");
                    // Playwright's SetInputFilesAsync on non-input elements simulates drag & drop
                    // Real DOM: <rich-textarea> is the chat input, <div data-test-id="textarea-inner"> wraps it
                    var dropTargets = new[] {
                        "rich-textarea",
                        "[data-test-id='textarea-inner']",
                        "[role='textbox']",
                        "[aria-label='Nhập câu lệnh cho Gemini']"
                    };
                    foreach (var target in dropTargets)
                    {
                        try
                        {
                            _log($"[PW-SCENE] 🔎 SetInputFiles on target: '{target}'");
                            var dropZone = _page!.Locator(target).First;
                            await dropZone.SetInputFilesAsync(filesToAttach.ToArray());
                            _log($"[PW-SCENE] ✅ Files drag & dropped onto '{target}'.");
                            await Task.Delay(2000);
                            return;
                        }
                        catch (Exception ex)
                        {
                            _log($"[PW-SCENE] ⚠️ SetInputFiles on '{target}' failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    _log("[PW-SCENE] ⚠️ Drag & drop did not find a suitable drop zone.");
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ Drag & drop block failed: {ex.GetType().Name}: {ex.Message}. Trying click-based upload...");
                }

                // Strategy 2: Click upload button in input-area → file chooser dialog
                // (after clicking the button, a menu pops up with 'Tải tệp lên' as the file-upload item)
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 2: Click input-area upload button → menu → file chooser...");

                    // Open upload menu first
                    bool menuOpened = false;
                    var uploadButtonSelectors = new[]
                    {
                        "button[aria-label='Nội dung tải lên và công cụ']",
                        "button[aria-label*='tải lên']",
                        "button[aria-label*='Upload files']",
                        "button[aria-label*='Upload']",
                        "button[aria-label*='Attach']",
                    };

                    foreach (var sel in uploadButtonSelectors)
                    {
                        try
                        {
                            _log($"[PW-SCENE] 🔎 Trying upload button: '{sel}'");
                            var btn = _page.Locator(sel).First;
                            await btn.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                            await Task.Delay(800);
                            _log($"[PW-SCENE] ✅ Clicked upload button: '{sel}'");
                            menuOpened = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            _log($"[PW-SCENE] ⚠️ Upload button '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    // Now look for menu item that opens file chooser
                    // Confirmed test: "Tải tệp lên" is role=menuitem
                    var fileMenuItemSelectors = new[]
                    {
                        "[role='menuitem']:has-text('Tải tệp lên')",
                        "[role='menuitem']:has-text('Upload files')",
                        "[role='menuitem']:has-text('Upload file')",
                        "[role='menuitem']:has-text('Attach file')",
                        "button:has-text('Tải tệp lên')",
                        "[aria-label*='Tải tệp lên']",
                    };

                    var fileChooser = await _page!.RunAndWaitForFileChooserAsync(async () =>
                    {
                        if (menuOpened)
                        {
                            foreach (var sel in fileMenuItemSelectors)
                            {
                                try
                                {
                                    _log($"[PW-SCENE] 🔎 Clicking menu item: '{sel}'");
                                    var item = _page.Locator(sel).First;
                                    await item.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                                    _log($"[PW-SCENE] ✅ Clicked menu item: '{sel}'");
                                    return;
                                }
                                catch (Exception ex)
                                {
                                    _log($"[PW-SCENE] ⚠️ Menu item '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                                }
                            }
                        }

                        // Fallback: text-based attach buttons
                        var attachTexts = new[] { "Thêm tệp", "Đính kèm", "Upload file", "Attach" };
                        foreach (var text in attachTexts)
                        {
                            try
                            {
                                _log($"[PW-SCENE] 🔎 Trying attach button text: '{text}'");
                                var btn = _page.Locator($"text=\"{text}\"").First;
                                await btn.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                                _log($"[PW-SCENE] ✅ Clicked: '{text}'");
                                return;
                            }
                            catch (Exception ex)
                            {
                                _log($"[PW-SCENE] ⚠️ Attach text '{text}' click failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                        var btns = new[] { "button[aria-label*='Thêm']", "button[aria-label*='Upload']", "[data-test-id='add-files']" };
                        foreach (var sel in btns)
                        {
                            try
                            {
                                var b = await _page.QuerySelectorAsync(sel);
                                if (b != null) { await b.ClickAsync(); _log($"[PW-SCENE] ✅ Clicked attach selector: '{sel}'"); return; }
                            }
                            catch (Exception ex) { _log($"[PW-SCENE] ⚠️ Attach selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}"); }
                        }
                    }, new PageRunAndWaitForFileChooserOptions { Timeout = 8000 });

                    if (fileChooser != null)
                    {
                        await fileChooser.SetFilesAsync(filesToAttach.ToArray());
                        _log($"[PW-SCENE] ✅ Attached {filesToAttach.Count} files via menu → file chooser.");
                        await Task.Delay(2500);
                        return;
                    }
                }
                catch (TimeoutException) { _log("[PW-SCENE] ⚠️ File chooser not triggered (timeout)."); }
                catch (Exception ex) { _log($"[PW-SCENE] ⚠️ File chooser flow failed: {ex.GetType().Name}: {ex.Message}"); }

                // Strategy 3: Direct file input
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 3: Direct file input element...");
                    var input = await _page!.QuerySelectorAsync("input[type='file']");
                    if (input != null)
                    {
                        await input.SetInputFilesAsync(filesToAttach.ToArray());
                        _log($"[PW-SCENE] ✅ Attached {filesToAttach.Count} files via file input.");
                        await Task.Delay(2000);
                        return;
                    }
                    _log("[PW-SCENE] ⚠️ No input[type='file'] found.");
                }
                catch (Exception ex) { _log($"[PW-SCENE] ⚠️ File input failed: {ex.GetType().Name}: {ex.Message}"); }

                // Strategy 4: JavaScript DataTransfer drag-drop on document.body
                // Based on the proven pattern from ChatGptService.SimulateDragDropFileAsync.
                // Gemini's drag overlay listens for events at the document/window level,
                // so targeting body ensures the drop event reaches every overlay handler.
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 4: JS DataTransfer drop event on document.body...");

                    // Build JSON payload (array of {name, type, b64})
                    var filePayload = new List<object>();
                    foreach (var p in filesToAttach)
                    {
                        var bytes = await File.ReadAllBytesAsync(p);
                        filePayload.Add(new
                        {
                            name = Path.GetFileName(p),
                            type = MimeTypeHelper.GetMimeType(p),
                            b64 = Convert.ToBase64String(bytes)
                        });
                    }
                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(filePayload);

                    // Adapted from DragDropJsTemplate in ChatGptService.cs (proven working).
                    // Key differences for Gemini:
                    //  - Target document.body so events bubble to window-level overlay
                    //  - Add small delay before drop (Gemini overlay needs to appear first)
                    //  - cleanOverlay runs at 500ms / 1500ms / 3000ms to dismiss stuck "Drop files" overlay
                    var jsResult = await _page!.EvaluateAsync(@"async (filesJson) => {
                        const files = JSON.parse(filesJson);
                        const targets = [
                            document.body,
                            document.querySelector('rich-textarea'),
                            document.querySelector('[data-test-id=""textarea-inner""]'),
                            document.documentElement
                        ].filter(Boolean);

                        const dt = new DataTransfer();
                        for (const f of files) {
                            const bin = atob(f.b64);
                            const arr = new Uint8Array(bin.length);
                            for (let i = 0; i < bin.length; i++) arr[i] = bin.charCodeAt(i);
                            const file = new File([arr], f.name, { type: f.type });
                            dt.items.add(file);
                        }

                        // Build events. bubbles+cancelable allow them to bubble up & be canceled.
                        const enter  = new DragEvent('dragenter',  { bubbles: true, cancelable: true, dataTransfer: dt });
                        const over   = new DragEvent('dragover',   { bubbles: true, cancelable: true, dataTransfer: dt });
                        const drop   = new DragEvent('drop',       { bubbles: true, cancelable: true, dataTransfer: dt });
                        const leave  = new DragEvent('dragleave',  { bubbles: true, cancelable: true, dataTransfer: dt });

                        let dispatched = 0;
                        for (const target of targets) {
                            try {
                                target.dispatchEvent(enter);
                                target.dispatchEvent(over);
                                target.dispatchEvent(drop);
                                dispatched++;
                            } catch (e) {}
                        }
                        // dragleave on every level to dismiss overlay
                        try { document.body.dispatchEvent(leave); window.dispatchEvent(leave); document.dispatchEvent(leave); } catch (e) {}

                        // Clean stuck 'Drop files' overlay (Gemini adds an absolute overlay during dragover)
                        const cleanOverlay = () => {
                            try {
                                const all = document.querySelectorAll('*');
                                for (const el of all) {
                                    const cs = window.getComputedStyle(el);
                                    if ((cs.position === 'fixed' || cs.position === 'absolute') &&
                                        el.textContent &&
                                        (el.textContent.includes('Drop') || el.textContent.includes('Thả') ||
                                         el.textContent.includes('thả tệp') || el.textContent.includes('tệp'))) {
                                        el.remove();
                                    }
                                }
                            } catch (e) {}
                        };
                        cleanOverlay();
                        setTimeout(cleanOverlay, 500);
                        setTimeout(cleanOverlay, 1500);
                        setTimeout(cleanOverlay, 3000);

                        return { ok: true, fileCount: files.length, dispatched };
                    }", payloadJson);

                    var resultJson = jsResult?.ToString() ?? "{}";
                    _log($"[PW-SCENE] 📎 JS drop result: {resultJson}");

                    await Task.Delay(4000); // Allow Gemini to process uploaded files
                    try
                    {
                        await _page!.ScreenshotAsync(new PageScreenshotOptions
                        {
                            Path = Path.Combine(Path.GetTempPath(), "pw_after_drop.png")
                        });
                    }
                    catch { }

                    if (resultJson.Contains("\"ok\":true"))
                    {
                        _log($"[PW-SCENE] ✅ Attached {filesToAttach.Count} files via JS body drop event.");
                        return;
                    }
                }
                catch (Exception ex) { _log($"[PW-SCENE] ⚠️ JS drop strategy failed: {ex.GetType().Name}: {ex.Message}"); }

                _log("[PW-SCENE] ❌ All attachment strategies failed. Continuing without files.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ File attachment failed: {ex.Message} — continuing without files.");
            }
        }

        private async Task TypePromptAsync(string prompt)
        {
            // Real Gemini DOM:
            // <rich-textarea class="text-input-field_textarea ql-container ql-bubble">
            //   <div class="ql-editor textarea new-input-ui" contenteditable="true"
            //        role="textbox" aria-label="Nhập câu lệnh cho Gemini">
            IElementHandle? inputElement = null;

            // Strategy 1: Use aria-label (most stable)
            try
            {
                _log("[PW-SCENE] 🔎 Strategy 1: aria-label='Nhập câu lệnh cho Gemini'");
                inputElement = await _page!.WaitForSelectorAsync(
                    "[aria-label='Nhập câu lệnh cho Gemini']",
                    new PageWaitForSelectorOptions { Timeout = 5000, State = WaitForSelectorState.Visible });
                if (inputElement != null) _log("[PW-SCENE] ✅ Found input via aria-label.");
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ aria-label selector failed: {ex.GetType().Name}: {ex.Message}");
            }

            // Strategy 2: Use ql-editor in rich-textarea
            if (inputElement == null)
            {
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 2: rich-textarea div.ql-editor[contenteditable='true']");
                    inputElement = await _page!.WaitForSelectorAsync(
                        "rich-textarea div.ql-editor[contenteditable='true']",
                        new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                    if (inputElement != null) _log("[PW-SCENE] ✅ Found input via ql-editor.");
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ ql-editor selector failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Strategy 3: Any contenteditable inside textarea-wrapper
            if (inputElement == null)
            {
                try
                {
                    _log("[PW-SCENE] 🔎 Strategy 3: [data-test-id='textarea-inner'] [contenteditable='true']");
                    inputElement = await _page!.WaitForSelectorAsync(
                        "[data-test-id='textarea-inner'] [contenteditable='true']",
                        new PageWaitForSelectorOptions { Timeout = 3000, State = WaitForSelectorState.Visible });
                    if (inputElement != null) _log("[PW-SCENE] ✅ Found input via textarea-inner.");
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ textarea-inner selector failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (inputElement == null)
            {
                _log("[PW-SCENE] ⚠️ Could not find input area. Using keyboard fallback.");
                await _page!.Keyboard.PressAsync("Tab");
                await Task.Delay(500);
                await _page!.Keyboard.TypeAsync(prompt, new KeyboardTypeOptions { Delay = 40 });
                await Task.Delay(800);
                _log($"[PW-SCENE] 📝 Prompt typed (keyboard fallback).");
                return;
            }

            // Type the prompt
            // Click + brief settle delay (Gemini needs ~300ms to attach focus & event listeners)
            await inputElement.ClickAsync();
            await Task.Delay(500);

            // Clear any residual text from previous attempts
            try
            {
                await _page!.Keyboard.PressAsync("Control+a");
                await Task.Delay(150);
                await _page!.Keyboard.PressAsync("Delete");
                await Task.Delay(300);
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Clear-text step failed: {ex.Message}");
            }

            // Human-like typing: 40ms/keystroke ≈ realistic typing pace
            // For ~270 char prompts this is ~11s, well within patience but not bot-fast.
            await _page!.Keyboard.TypeAsync(prompt, new KeyboardTypeOptions { Delay = 40 });
            await Task.Delay(800); // let Gemini's internal state settle

            // Verify typed length matches
            try
            {
                var typedLen = await _page!.EvaluateAsync<int>(@"() => {
                    const inp = document.querySelector('[aria-label=""Nhập câu lệnh cho Gemini""]')
                        || document.querySelector('.ql-editor')
                        || document.querySelector('[contenteditable=""true""]');
                    if (!inp) return -1;
                    return (inp.innerText || inp.textContent || '').length;
                }");
                var expectedLen = prompt?.Length ?? 0;
                _log($"[PW-SCENE] 🔎 DOM typed length: {typedLen}, expected: {expectedLen}");

                if (typedLen >= 0 && expectedLen > 0 && typedLen < expectedLen * 0.85)
                {
                    _log($"[PW-SCENE] ⚠️ Only {typedLen}/{expectedLen} chars typed. Re-typing...");
                    await inputElement.ClickAsync();
                    await Task.Delay(300);
                    await _page!.Keyboard.PressAsync("Control+a");
                    await Task.Delay(150);
                    await _page!.Keyboard.PressAsync("Delete");
                    await Task.Delay(300);
                    await _page!.Keyboard.TypeAsync(prompt, new KeyboardTypeOptions { Delay = 40 });
                    await Task.Delay(800);
                }
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Length verification failed: {ex.Message}");
            }

            _log($"[PW-SCENE] 📝 Prompt typed.");
        }

        private async Task ClickSendAsync()
        {
            // Step 0: Close any open menu/overlay (upload menu may still be open from file attach)
            try
            {
                _log("[PW-SCENE] 🔎 Pre-send: pressing Escape to close any open menu/overlay...");
                await _page!.Keyboard.PressAsync("Escape");
                await Task.Delay(400);
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Escape failed: {ex.Message}");
            }

            // Step 0b: Verify prompt text length before sending (avoid sending half-typed prompt)
            try
            {
                var domTextLen = await _page!.EvaluateAsync<int>(@"() => {
                    const inp = document.querySelector('[aria-label=""Nhập câu lệnh cho Gemini""]')
                        || document.querySelector('.ql-editor')
                        || document.querySelector('[contenteditable=""true""]');
                    if (!inp) return -1;
                    return (inp.innerText || inp.textContent || '').trim().length;
                }");
                _log($"[PW-SCENE] 🔎 Pre-send DOM text length: {domTextLen} chars");
                if (domTextLen < 50)
                {
                    _log($"[PW-SCENE] ⚠️ DOM has only {domTextLen} chars (too short). Waiting 1s for typing to complete...");
                    await Task.Delay(1000);
                    var retryLen = await _page!.EvaluateAsync<int>(@"() => {
                        const inp = document.querySelector('[aria-label=""Nhập câu lệnh cho Gemini""]')
                            || document.querySelector('.ql-editor')
                            || document.querySelector('[contenteditable=""true""]');
                        if (!inp) return -1;
                        return (inp.innerText || inp.textContent || '').trim().length;
                    }");
                    _log($"[PW-SCENE] 🔎 Re-checked DOM length: {retryLen} chars");
                }
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Pre-send length check failed: {ex.Message}");
            }

            var sendSelectors = new[]
            {
                // Most reliable: EXACT match on aria-label (substring match with Vietnamese
                // precomposed Unicode can fail in Playwright). Confirmed working in test.
                "button[aria-label='Gửi tin nhắn']",
                "button[aria-label='Send message']",
                // Substring match (English only — Vietnamese has Unicode normalization issues)
                "button[aria-label*='Send message']",
                "button[aria-label*='Send']",
                // Icon-based
                "button:has(svg[data-test-id='send-icon'])",
                "button:has(svg[aria-label*='send-icon'])",
                // Material Design
                ".mat-mdc-button:has(.material-icons)",
                // Class-based
                "button[class*='send-button']",
                "button.input-area-send-button",
                // Legacy
                "[data-test-id='send-button']",
                "button.send-button",
                // Position-based fallback
                "[data-test-id='input-action-bar'] button:last-child",
                ".input-action-bar button:last-child",
                "footer button:last-child"
            };

            foreach (var sel in sendSelectors)
            {
                try
                {
                    _log($"[PW-SCENE] 🔎 Trying send button selector: '{sel}'");
                    var btn = await _page!.WaitForSelectorAsync(sel, new PageWaitForSelectorOptions
                    {
                        Timeout = 3000,
                        State = WaitForSelectorState.Visible
                    });
                    if (btn != null)
                    {
                        // Check visibility on screen (not occluded by overlay)
                        var box = await btn.BoundingBoxAsync();
                        if (box != null)
                        {
                            _log($"[PW-SCENE] 📐 send button bounding box: x={box.X}, y={box.Y}, w={box.Width}, h={box.Height}");
                        }
                        await btn.ClickAsync(new ElementHandleClickOptions { Timeout = 3000 });
                        _log($"[PW-SCENE] ▶️ Prompt sent via selector '{sel}'.");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ Send selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Fallback: press Enter
            await _page!.Keyboard.PressAsync("Enter");
            _log("[PW-SCENE] ▶️ Prompt sent (Enter key).");
        }

        private async Task<(string Text, string? Thoughts)> WaitForResponseAsync(TimeSpan? maxWait = null)
        {
            _log("[PW-SCENE] ⏳ Waiting for Gemini response...");

            // Initial delay for Gemini to start processing
            await Task.Delay(3000);

            var startTime = DateTime.Now;
            // Extended thinking mode can take 2-5 minutes of "Thinking for Xs..." before
            // the actual response text starts streaming. 10 min is safer default.
            var wait = maxWait ?? TimeSpan.FromMinutes(10);
            string lastText = "";
            string? lastThoughts = null;
            int stableCount = 0;
            const int REQUIRED_STABLE_CHECKS = 3;
            const int POLL_INTERVAL_MS = 4000;
            // Tolerance for "stable" comparison: Gemini's token counter updates even
            // when content is technically done, so |Δ| < 8 chars is "stable enough".
            const int STABLE_TOLERANCE_CHARS = 8;

            while (DateTime.Now - startTime < wait)
            {
                await Task.Delay(POLL_INTERVAL_MS);

                try
                {
                    // PRIMARY signal: check if Gemini is ready (send button visible).
                    // This is the definitive state transition: Gemini's internal state machine
                    // flips from "generating" → "ready" ONLY when the response is fully complete.
                    bool isDone = await IsResponseCompleteAsync();

                    // SECONDARY: also check stop button (still generating)
                    bool isStillGenerating = await IsGeminiStillGeneratingAsync();

                    if (isStillGenerating)
                    {
                        _log("[PW-SCENE] ⏳ Gemini still generating...");
                        stableCount = 0;
                    }

                    // Extract response text using multiple strategies
                    string currentText = await ExtractResponseTextAsync();

                    // Extract thinking/thoughts
                    string? currentThoughts = await ExtractThoughtsAsync();

                    // Check if we have meaningful content
                    bool hasContent = !string.IsNullOrWhiteSpace(currentText) && currentText.Length >= RESPONSE_MIN_CHARS;

                    // DEFINITIVE completion: send button visible + substantial content
                    // → Gemini is ready + response is rendered → NO need for stability checks
                    if (isDone && hasContent)
                    {
                        _log($"[PW-SCENE] ✅ Response complete (send button visible, {currentText.Length} chars).");
                        string? finalThoughts = string.IsNullOrWhiteSpace(currentThoughts) ? lastThoughts : currentThoughts;
                        return (currentText, string.IsNullOrWhiteSpace(finalThoughts) ? null : finalThoughts);
                    }

                    // No content yet — Gemini may still be loading/starting
                    if (!hasContent)
                    {
                        if (!isStillGenerating && !isDone)
                        {
                            _log("[PW-SCENE] ⚠️ No content yet, Gemini may still be loading...");
                        }
                        continue;
                    }

                    // Fallback stability check: when we have content but send button not yet visible.
                    // This handles edge cases where the button state transition is delayed.
                    int delta = Math.Abs(currentText.Length - lastText.Length);
                    bool isStable = delta < STABLE_TOLERANCE_CHARS;

                    if (isStable)
                    {
                        stableCount++;
                        _log($"[PW-SCENE] 📊 Stable check {stableCount}/{REQUIRED_STABLE_CHECKS} (text={currentText.Length} chars, Δ={delta})");

                        if (stableCount >= REQUIRED_STABLE_CHECKS)
                        {
                            _log($"[PW-SCENE] ✅ Response stable after {stableCount} checks.");
                            string? finalThoughts = string.IsNullOrWhiteSpace(currentThoughts) ? lastThoughts : currentThoughts;
                            return (currentText, string.IsNullOrWhiteSpace(finalThoughts) ? null : finalThoughts);
                        }
                    }
                    else if (currentText.Length != lastText.Length)
                    {
                        _log($"[PW-SCENE] 📝 Response updated: {lastText.Length} → {currentText.Length} chars (Δ={delta})");
                        stableCount = 0;
                    }

                    lastText = currentText;
                    if (!string.IsNullOrWhiteSpace(currentThoughts))
                    {
                        lastThoughts = currentThoughts;
                    }
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ Poll error: {ex.Message}");
                }
            }

            _log($"[PW-SCENE] ⏰ Timeout after {wait.TotalMinutes:F1} min waiting for response (lastText={lastText.Length} chars).");
            throw new TimeoutException(
                $"Gemini response did not complete within {wait.TotalMinutes:F1} minutes. " +
                $"Last captured text length: {lastText.Length} chars. " +
                $"Caller may retry by reopening the chat URL and sending 'Continue'.");
        }

        /// <summary>
        /// Checks if Gemini is still generating a response.
        /// PRIMARY: check for stop button (definitive — it only appears while generating).
        /// FALLBACK: spinner / progressbar (may appear in other contexts).
        /// Uses 1 EvaluateAsync call instead of 7 QuerySelectorAsync round-trips.
        /// </summary>
        private async Task<bool> IsGeminiStillGeneratingAsync()
        {
            try
            {
                // Single EvaluateAsync — check stop button state in one Chrome round-trip.
                // From actual HTML: aria-label="Ngừng tạo câu trả lời", inside data-test-id="send-button-container", class contains "stop".
                // Also supports English "Stop generating response".
                var result = await _page!.EvaluateAsync<bool>(@"() => {
                    const stopSelectors = [
                        'button[aria-label=""Ngừng tạo câu trả lời""]',
                        'button[aria-label=""Stop generating response""]',
                        'button[aria-label*=""Stop generating""]',
                        '[data-test-id=""send-button-container""] .stop button',
                        '.stop button[aria-label*=""Stop""]'
                    ];
                    for (const sel of stopSelectors) {
                        try {
                            const el = document.querySelector(sel);
                            if (el && el.offsetParent !== null) return true;
                        } catch(e) {}
                    }
                    return false;
                }");

                if (result)
                {
                    _log("[PW-SCENE] 🔎 Gemini still generating (stop button visible)");
                    return true;
                }

                // Fallback: check spinner / progressbar (less reliable — may appear in other contexts)
                var fallbackSelectors = new[]
                {
                    "mat-spinner",
                    "[role='progressbar']",
                    ".loading-indicator"
                };

                foreach (var sel in fallbackSelectors)
                {
                    try
                    {
                        var el = await _page!.QuerySelectorAsync(sel);
                        if (el != null && await el.IsVisibleAsync())
                        {
                            _log($"[PW-SCENE] 🔎 Gemini still generating (fallback: '{sel}')");
                            return true;
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if Gemini has finished generating and is ready for next prompt.
        /// DEFINITIVE SIGNAL: send button is visible = Gemini state machine is "ready".
        /// This is more reliable than stability heuristics (Δ < N chars).
        /// Returns true when the input area is ready for a new prompt.
        /// </summary>
        private async Task<bool> IsResponseCompleteAsync()
        {
            try
            {
                // Single EvaluateAsync — check send button state.
                // From actual HTML: data-test-id="send-button-container" with "visible" class.
                // aria-label="Gửi tin nhắn" or "Send message" when idle.
                var result = await _page!.EvaluateAsync<bool>(@"() => {
                    const sendSelectors = [
                        // Primary: exact aria-label matches
                        'button[aria-label=""Gửi tin nhắn""]',
                        'button[aria-label=""Send message""]',
                        // Partial match (English)
                        'button[aria-label*=""Send message""]',
                        'button[aria-label*=""Send""]',
                        // Container-based (from actual HTML: send-button-container + visible class)
                        '[data-test-id=""send-button-container""].visible button',
                        // General send button in the container
                        '[data-test-id=""send-button-container""] button[aria-label*=""Gửi""]',
                        '[data-test-id=""send-button-container""] button[aria-label*=""Send""]'
                    ];
                    for (const sel of sendSelectors) {
                        try {
                            const el = document.querySelector(sel);
                            // Check if visible (offsetParent !== null means not display:none/visibility:hidden)
                            if (el && el.offsetParent !== null) {
                                // Also verify it's NOT a stop button (class should NOT contain 'stop')
                                const container = el.closest('[data-test-id=""send-button-container""]');
                                if (container && container.classList.contains('stop')) continue;
                                // And NOT aria-disabled
                                if (el.getAttribute('aria-disabled') === 'true') continue;
                                return true;
                            }
                        } catch(e) {}
                    }
                    return false;
                }");

                if (result)
                {
                    _log("[PW-SCENE] ✅ Response complete (send button visible)");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ IsResponseCompleteAsync failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extracts the latest assistant response text from the Gemini conversation page.
        /// Uses multiple selector strategies to handle DOM changes.
        /// IMPORTANT: must exclude "Thinking" section when thinking mode is enabled,
        /// otherwise the live "Thinking for Xs..." indicator pollutes the text length.
        ///
        /// Returns EMPTY string if response is not yet ready (e.g. still thinking,
        /// or only a tiny status chip is visible). Caller should keep polling.
        /// Returns content >= RESPONSE_MIN_CHARS once the actual model response
        /// (post-thinking) is rendered.
        /// </summary>
        private const int RESPONSE_MIN_CHARS = 500;

        private async Task<string> ExtractResponseTextAsync()
        {
            try
            {
                // Strategy 0: Look for response text EXCLUDING thinking section.
                // This is the most reliable when thinking mode is on.
                try
                {
                    var responseOnly = await _page!.EvaluateAsync<string>(@"() => {
                        // Find ALL conversation turns
                        const turns = document.querySelectorAll(
                            '[data-test-id=""conversation-turn""],' +
                            '[data-message-role=""model""],' +
                            '[data-message-role=""assistant""]'
                        );
                        if (turns.length === 0) return null;

                        // Take the LAST turn (most recent)
                        const last = turns[turns.length - 1];

                        // Clone so we can remove thinking section without touching DOM
                        const clone = last.cloneNode(true);
                        const thinkingEls = clone.querySelectorAll(
                            '[data-test-id=""thinking-section""],' +
                            '[data-test-id=""model-thinking""],' +
                            '.thinking-content,' +
                            '[aria-label*=""thinking process"" i],' +
                            '[aria-label*=""quá trình suy nghĩ"" i]'
                        );
                        thinkingEls.forEach(el => el.remove());

                        // Also remove any button-text like 'Show thinking' / 'Ẩn suy nghĩ'
                        const allButtons = clone.querySelectorAll('button');
                        allButtons.forEach(b => {
                            const t = (b.textContent || '').trim();
                            if (/^(show|hide|hiện|ẩn)\s*(thinking|suy nghĩ|thoughts)/i.test(t)) {
                                b.remove();
                            }
                        });

                        return (clone.innerText || clone.textContent || '').trim();
                    }");

                    // Require substantial content. During "thinking", this returns ~0-100 chars
                    // (the thinking indicator). Wait for the real response, which is thousands.
                    if (!string.IsNullOrWhiteSpace(responseOnly) && responseOnly.Length >= RESPONSE_MIN_CHARS)
                    {
                        _log($"[PW-SCENE] ✅ Extracted response-excluding-thinking via clone ({responseOnly.Length} chars)");
                        return responseOnly;
                    }
                    _log($"[PW-SCENE] ⏳ Strategy 0: only {responseOnly?.Length ?? 0} chars (still thinking, need >= {RESPONSE_MIN_CHARS}).");
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ Strategy 0 (exclude-thinking) failed: {ex.Message}");
                }

                // Strategy 1: Look for the last assistant message turn
                // Gemini uses [data-test-id="conversation-turn"] or similar for each message
                var turnSelectors = new[]
                {
                    "[data-test-id='conversation-turn']",
                    "[data-message-role='model']",
                    "[data-message-role='assistant']",
                    ".model-turn",
                    "message-content"
                };

                foreach (var sel in turnSelectors)
                {
                    try
                    {
                        var turns = await _page!.QuerySelectorAllAsync(sel);
                        if (turns.Count > 0)
                        {
                            _log($"[PW-SCENE] 🔎 Found {turns.Count} turns via '{sel}'");
                            // Get the LAST turn (most recent assistant response)
                            var lastTurn = turns[turns.Count - 1];
                            var text = await lastTurn.TextContentAsync();
                            // Same threshold: skip tiny text — it's probably the thinking indicator
                            if (!string.IsNullOrWhiteSpace(text) && text.Length >= RESPONSE_MIN_CHARS)
                            {
                                _log($"[PW-SCENE] ✅ Extracted response text via '{sel}' ({text.Length} chars)");
                                return text.Trim();
                            }
                            _log($"[PW-SCENE] ⏳ Last turn text via '{sel}' is {text?.Length ?? 0} chars (< {RESPONSE_MIN_CHARS}); still thinking.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Turn selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Strategy 2: Look for response content containers directly
                var contentSelectors = new[]
                {
                    ".response-content",
                    ".model-response",
                    "[data-test-id='model-response']",
                    ".assistant-message",
                    "div.markdown"
                };

                foreach (var sel in contentSelectors)
                {
                    try
                    {
                        var elements = await _page!.QuerySelectorAllAsync(sel);
                        if (elements.Count > 0)
                        {
                            _log($"[PW-SCENE] 🔎 Found {elements.Count} content elements via '{sel}'");
                            // Get last element's text (most recent response)
                            var lastEl = elements[elements.Count - 1];
                            var text = await lastEl.TextContentAsync();
                            if (!string.IsNullOrWhiteSpace(text) && text.Length >= RESPONSE_MIN_CHARS)
                            {
                                _log($"[PW-SCENE] ✅ Extracted response text via content '{sel}' ({text.Length} chars)");
                                return text.Trim();
                            }
                            _log($"[PW-SCENE] ⏳ Content '{sel}' last element is {text?.Length ?? 0} chars (< {RESPONSE_MIN_CHARS}); still thinking.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Content selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // REMOVED Strategy 3 (main, [role=main]) and Strategy 4 (body) —
                // both catch the ENTIRE page (sidebar, header, history, input) which is
                // ~6000-7000 chars and is NOT the actual response. With those strategies
                // the system "stops" after the first poll, returning the wrong text
                // while Gemini is still thinking.
                //
                // Strategy: return empty. Caller (WaitForResponseAsync) will keep polling
                // until the real response (>= 500 chars) is rendered.
                return "";
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ ExtractResponseText error: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Extracts thinking/thoughts from Gemini's response.
        /// Gemini shows thinking in a collapsible section (like "Show thinking" / "Ẩn suy nghĩ").
        /// </summary>
        private async Task<string?> ExtractThoughtsAsync()
        {
            try
            {
                // Strategy 1: Look for thinking section with specific data-test-id
                var thoughtSelectors = new[]
                {
                    "[data-test-id='thinking-section']",
                    "[data-test-id='model-thinking']",
                    ".thinking-content",
                    "[aria-label*='thinking process']",
                    "[aria-label*='quá trình suy nghĩ']"
                };

                foreach (var sel in thoughtSelectors)
                {
                    try
                    {
                        var el = await _page!.QuerySelectorAsync(sel);
                        if (el != null)
                        {
                            var text = await el.TextContentAsync();
                            if (!string.IsNullOrWhiteSpace(text) && text.Length > 20)
                            {
                                _log($"[PW-SCENE] ✅ Found thoughts via '{sel}' ({text.Length} chars)");
                                return text.Trim();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Thought selector '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Strategy 2: Find buttons that toggle thinking visibility
                var toggleSelectors = new[]
                {
                    "button:has-text('Suy nghĩ')",
                    "button:has-text('Thinking')",
                    "button:has-text('Show thought')",
                    "button:has-text('Ẩn suy nghĩ')",
                    "button:has-text('Hide thinking')",
                    "button[aria-label*='thinking']",
                    "button[aria-label*='suy nghĩ']"
                };

                foreach (var sel in toggleSelectors)
                {
                    try
                    {
                        var btn = await _page!.QuerySelectorAsync(sel);
                        if (btn != null)
                        {
                            // Click to expand if collapsed
                            try
                            {
                                var btnText = await btn.TextContentAsync();
                                bool isExpandAction = btnText?.Contains("Show", StringComparison.OrdinalIgnoreCase) == true ||
                                                      btnText?.Contains("Hiện", StringComparison.OrdinalIgnoreCase) == true;

                                if (isExpandAction)
                                {
                                    await btn.ClickAsync();
                                    await Task.Delay(1000);
                                }
                            }
                            catch (Exception ex)
                            {
                                _log($"[PW-SCENE] ⚠️ Toggle expand for '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                            }

                            // Now find the expanded thinking content nearby
                            try
                            {
                                // Get the parent container and extract text
                                var parent = await btn.EvaluateHandleAsync("el => el.closest('[data-test-id=\"conversation-turn\"], .model-turn, .turn-container')");
                                if (parent != null)
                                {
                                    var parentEl = parent.AsElement();
                                    if (parentEl != null)
                                    {
                                        var text = await parentEl.TextContentAsync();
                                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 30)
                                        {
                                            _log($"[PW-SCENE] ✅ Found thoughts via toggle '{sel}' ({text.Length} chars)");
                                            // Strip the button text itself from the content
                                            return text.Trim();
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _log($"[PW-SCENE] ⚠️ Parent evaluate for '{sel}' failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log($"[PW-SCENE] ⚠️ Toggle selector '{sel}' query failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                // Strategy 3: Look for <details>/<summary> elements (Gemini sometimes uses these)
                try
                {
                    var detailsElements = await _page!.QuerySelectorAllAsync("details");
                    foreach (var details in detailsElements)
                    {
                        try
                        {
                            var summary = await details.QuerySelectorAsync("summary");
                            if (summary != null)
                            {
                                var summaryText = await summary.TextContentAsync();
                                if (summaryText != null &&
                                    (summaryText.Contains("Think", StringComparison.OrdinalIgnoreCase) ||
                                     summaryText.Contains("Suy nghĩ", StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Ensure it's open
                                    var isOpen = await details.GetAttributeAsync("open");
                                    if (isOpen == null)
                                    {
                                        await summary.ClickAsync();
                                        await Task.Delay(500);
                                    }
                                    var text = await details.TextContentAsync();
                                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 30)
                                    {
                                        _log($"[PW-SCENE] ✅ Found thoughts via <details>/<summary> ({text.Length} chars)");
                                        return text.Trim();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _log($"[PW-SCENE] ⚠️ details element processing failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log($"[PW-SCENE] ⚠️ <details> query failed: {ex.GetType().Name}: {ex.Message}");
                }

                return null;
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ ExtractThoughts error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Captures the current Gemini chat URL before closing the browser.
        /// Format: https://gemini.google.com/gem/{gemId}/cb{chatId} or https://gemini.google.com/app/{chatId}
        /// Returns null if URL doesn't match the expected pattern.
        /// </summary>
        private async Task<string?> GetCurrentChatUrlAsync()
        {
            try
            {
                var currentUrl = _page?.Url;
                if (string.IsNullOrWhiteSpace(currentUrl))
                {
                    _log("[PW-SCENE] ⚠️ Cannot capture chat URL: page URL is null/empty.");
                    return null;
                }

                // Gemini chat URLs follow pattern:
                //   https://gemini.google.com/gem/{gemId}/{chatSuffix}
                //   https://gemini.google.com/app/{chatSuffix}
                // Where chatSuffix starts with "cb" (chat-bearing) or contains a chat ID.
                if (currentUrl.Contains("/gem/") || currentUrl.Contains("/app/"))
                {
                    _log($"[PW-SCENE] 📌 Captured chat URL: {currentUrl}");
                    return currentUrl;
                }

                _log($"[PW-SCENE] ⚠️ Current URL doesn't look like a chat URL: {currentUrl}");
                return null;
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ⚠️ Failed to capture chat URL: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Types "Continue" into the prompt input and clicks send.
        /// Used to resume a long-running generation that timed out.
        /// </summary>
        private async Task<bool> SendContinueMessageAsync()
        {
            try
            {
                _log("[PW-SCENE] 💬 Sending 'Continue' message to resume generation...");

                // Find the prompt input area (same logic as TypePromptAsync)
                var inputSelectors = new[]
                {
                    "div[contenteditable='true']",
                    "rich-textarea .ql-editor",
                    ".ql-editor",
                    "textarea[aria-label*='prompt' i]",
                    "textarea[aria-label*='nhập' i]",
                    "textarea[aria-label*='message' i]"
                };

                IElementHandle? input = null;
                foreach (var sel in inputSelectors)
                {
                    try
                    {
                        input = await _page!.QuerySelectorAsync(sel);
                        if (input != null) break;
                    }
                    catch { }
                }

                if (input == null)
                {
                    _log("[PW-SCENE] ❌ Cannot find prompt input to type 'Continue'.");
                    return false;
                }

                // Focus + clear + type
                await input.ClickAsync();
                await Task.Delay(500);
                await _page!.Keyboard.PressAsync("Control+A");
                await _page!.Keyboard.PressAsync("Delete");
                await Task.Delay(300);
                await _page!.Keyboard.TypeAsync("Continue", new KeyboardTypeOptions { Delay = 50 });
                await Task.Delay(500);

                // Click Send
                await ClickSendAsync();
                _log("[PW-SCENE] ▶️ 'Continue' message sent.");
                return true;
            }
            catch (Exception ex)
            {
                _log($"[PW-SCENE] ❌ Failed to send 'Continue': {ex.Message}");
                return false;
            }
        }

        private async Task CleanupAsync()
        {
            try
            {
                if (_context != null)
                {
                    await _context.CloseAsync();
                    _context = null;
                }
            }
            catch { }
            _playwright?.Dispose();
            _playwright = null;
            _page = null;
            _log("[PW-SCENE] 🧹 Browser cleaned up.");
        }
    }

    /// <summary>
    /// Tiny helper to guess MIME type from extension. Used for JS drop-event file payload.
    /// </summary>
    internal static class MimeTypeHelper
    {
        public static string GetMimeType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".srt" => "application/x-subrip",
                ".vtt" => "text/vtt",
                ".pdf" => "application/pdf",
                ".json" => "application/json",
                ".csv" => "text/csv",
                ".html" or ".htm" => "text/html",
                ".xml" => "application/xml",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream"
            };
        }
    }
}
