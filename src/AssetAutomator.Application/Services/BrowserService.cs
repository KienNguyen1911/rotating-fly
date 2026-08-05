using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Manages Playwright browser lifecycle: initialization, slot allocation, profile cloning, and cleanup.
    /// Extracted from MainWindow.AutomationSteps.cs to follow SRP.
    /// </summary>
    public class BrowserService : IBrowserService
    {
        private IPlaywright? _playwright;
        private readonly ConcurrentDictionary<string, IBrowserContext> _browserContexts = new();
        private readonly SemaphoreSlim _browserInitSemaphore = new(1, 1);
        private static readonly bool[] _activeBrowserSlots = new bool[32];
        private static readonly object _browserSlotsLock = new object();
        private readonly Action<string> _log;

        public ConcurrentDictionary<string, IBrowserContext> BrowserContexts => _browserContexts;

        public BrowserService(Action<string> log)
        {
            _log = log;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private static double GetScreenWidth() => GetSystemMetrics(0);  // SM_CXSCREEN = 0
        private static double GetScreenHeight() => GetSystemMetrics(1);  // SM_CYSCREEN = 1

        /// <summary>
        /// <summary>
        /// Copies a Chrome profile directory, skipping cache folders to reduce size.
        /// </summary>
        public void CopyProfileDirectory(string sourceDir, string destinationDir)
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

        /// <summary>
        /// Lightweight copy of only the essential files needed for cookie extraction from a Chrome profile.
        /// Copies: Cookies, Cookies-journal, Local State, Network/Cookies, Network/Trust Tokens.
        /// Skips: Extensions, IndexedDB, Local Storage, Service Workers, etc. (hundreds of MB).
        /// </summary>
        public void CopyMinimalProfileForCookies(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            // Only copy essential root-level files
            string[] essentialFiles = { "Cookies", "Cookies-journal", "Local State" };
            foreach (string fileName in essentialFiles)
            {
                string srcFile = Path.Combine(sourceDir, fileName);
                string dstFile = Path.Combine(destinationDir, fileName);
                try
                {
                    if (File.Exists(srcFile))
                        File.Copy(srcFile, dstFile, true);
                }
                catch { }
            }

            // Only copy Network subfolder (contains cookie-related state)
            string networkSrc = Path.Combine(sourceDir, "Network");
            if (Directory.Exists(networkSrc))
            {
                string networkDst = Path.Combine(destinationDir, "Network");
                Directory.CreateDirectory(networkDst);
                foreach (string file in Directory.GetFiles(networkSrc))
                {
                    try
                    {
                        File.Copy(file, Path.Combine(networkDst, Path.GetFileName(file)), true);
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Initializes or reuses a browser context for the given profile path.
        /// Allocates a screen slot for window positioning and injects anti-detection scripts.
        /// </summary>
        public async Task<IBrowserContext> EnsureBrowserInitializedAsync(string profilePath)
        {
            await _browserInitSemaphore.WaitAsync();
            try
            {
                if (_browserContexts.TryGetValue(profilePath, out var existingContext))
                {
                    _log("Reusing existing browser session for profile: " + Path.GetFileName(profilePath));
                    return existingContext;
                }

                _log("Initializing browser session for profile: " + Path.GetFileName(profilePath));
                if (_playwright == null)
                {
                    _playwright = await Playwright.CreateAsync();
                }

                _log($"Launching browser using Chrome profile path: {profilePath}");
                if (!Directory.Exists(profilePath))
                {
                    _log($"[WARNING] Profile path does not exist. Creating directory: {profilePath}");
                    Directory.CreateDirectory(profilePath);
                }

                // Allocate a window position slot
                int windowSlot = -1;
                lock (_browserSlotsLock)
                {
                    for (int i = 0; i < _activeBrowserSlots.Length; i++)
                    {
                        if (!_activeBrowserSlots[i])
                        {
                            _activeBrowserSlots[i] = true;
                            windowSlot = i;
                            break;
                        }
                    }
                }

                double screenWidth = GetScreenWidth();
                double screenHeight = GetScreenHeight();
                int cols = 2;
                int rows = 2;
                int w = (int)(screenWidth / cols);
                int h = (int)(screenHeight / rows) - 40;
                int x = 0;
                int y = 0;
                if (windowSlot >= 0)
                {
                    x = (windowSlot % cols) * w;
                    y = (windowSlot / cols) * h;
                }

                var launchArgs = new List<string>
                {
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-infobars"
                };

                if (windowSlot >= 0)
                {
                    launchArgs.Add($"--window-position={x},{y}");
                    launchArgs.Add($"--window-size={w},{h}");
                }

                try
                {
                    var browserContext = await _playwright.Chromium.LaunchPersistentContextAsync(
                        profilePath,
                        new BrowserTypeLaunchPersistentContextOptions
                        {
                            Headless = false,
                            Channel = "chrome",
                            Args = launchArgs.ToArray()
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
                        _log($"Browser window closed for profile: {Path.GetFileName(profilePath)}. Cleaning up resources...");
                        _browserContexts.TryRemove(profilePath, out _);
                        if (windowSlot >= 0)
                        {
                            lock (_browserSlotsLock)
                            {
                                if (windowSlot < _activeBrowserSlots.Length)
                                {
                                    _activeBrowserSlots[windowSlot] = false;
                                }
                            }
                        }
                    };

                    _browserContexts[profilePath] = browserContext;
                    _log($"Browser session initialized successfully in slot {windowSlot} at ({x},{y}).");
                    return browserContext;
                }
                catch (Exception ex)
                {
                    _log($"[ERROR] Failed to start browser. Make sure Chrome is closed if using a personal profile. Detail: {ex.Message}");
                    if (windowSlot >= 0)
                    {
                        lock (_browserSlotsLock)
                        {
                            if (windowSlot < _activeBrowserSlots.Length)
                            {
                                _activeBrowserSlots[windowSlot] = false;
                            }
                        }
                    }
                    throw;
                }
            }
            finally
            {
                _browserInitSemaphore.Release();
            }
        }

        /// <summary>
        /// Safely closes a browser context with a 15-second timeout to prevent hanging.
        /// Consolidates duplicated close logic from RunSingleVideoFlowAsync.
        /// </summary>
        public async Task CloseBrowserSafelyAsync(IBrowserContext? context, string profilePath, Action<string>? taskLog = null)
        {
            if (context == null) return;

            var log = taskLog ?? _log;
            log("Closing browser instance...");
            _browserContexts.TryRemove(profilePath, out _);
            try
            {
                var closeTask = context.CloseAsync();
                if (await Task.WhenAny(closeTask, Task.Delay(Timeouts.BrowserCloseTimeoutMs)) != closeTask)
                {
                    log($"Browser close timed out after {Timeouts.BrowserCloseTimeoutMs / 1000}s, continuing anyway...");
                }
            }
            catch (Exception ex)
            {
                log($"Browser close error (non-fatal): {ex.Message}");
            }
            log("Browser instance closed successfully.");
        }

        /// <summary>
        /// Cleans up a temporary profile directory, suppressing errors.
        /// </summary>
        public void CleanupTempProfile(string tempProfilePath, Action<string>? taskLog = null)
        {
            if (!string.IsNullOrEmpty(tempProfilePath) && Directory.Exists(tempProfilePath))
            {
                var log = taskLog ?? _log;
                log("Cleaning up temporary profile folder...");
                try
                {
                    Directory.Delete(tempProfilePath, true);
                }
                catch { }
            }
        }
    }
}
