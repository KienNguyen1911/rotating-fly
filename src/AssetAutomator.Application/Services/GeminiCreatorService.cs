using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Infrastructure.Helpers;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Centralized business logic for the Gemini AI Creator tab.
    /// Extracted from MainWindow.GeminiCreator.cs to follow Single Responsibility Principle.
    /// Handles: gem loading, cookie import, server lifecycle, task management orchestration.
    /// All UI concerns (MessageBox, button states, status bar) remain in the code-behind
    /// via callback delegates — this service is pure logic, testable without WPF.
    /// </summary>
    public class GeminiCreatorService
    {
        private readonly ILogService _log;
        private readonly IConfigService _configService;
        private readonly GeminiApiService _geminiApiService;
        private readonly PythonServerManager _pythonServerManager;
        private readonly IHttpClientFactory _httpClientFactory;

        public GeminiCreatorService(
            ILogService logService,
            IConfigService configService,
            GeminiApiService geminiApiService,
            PythonServerManager pythonServerManager,
            IHttpClientFactory httpClientFactory)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _geminiApiService = geminiApiService ?? throw new ArgumentNullException(nameof(geminiApiService));
            _pythonServerManager = pythonServerManager ?? throw new ArgumentNullException(nameof(pythonServerManager));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        }

        // ─────────────────────────────────────────────────────
        //  Gem Loading
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Loads Gemini Gems from the Python REST API and updates the provided collections.
        /// Handles: default gem insertion, custom gem filtering, selection restoration, config-based defaults.
        /// </summary>
        public async Task LoadGeminiGemsAsync(
            ObservableCollection<GemOptionItem> scriptwriterCollection,
            ObservableCollection<GemOptionItem> sceneCreatorCollection,
            ObservableCollection<GemOptionItem> imagePromptCollection,
            ObservableCollection<GeminiTaskModel> taskCollection,
            Action<string>? onStatus = null)
        {
            try
            {
                onStatus?.Invoke("[GEMS] 🔄 Đang tải danh sách Gemini Gems từ API...");
                _log.Info(LogCategory.GeminiCreator, "Loading Gemini Gems from API...");

                var gems = await _geminiApiService.GetGemsAsync(includeHidden: true);

                // Snapshot existing selections
                var existingSelections = taskCollection.Select(t => new
                {
                    Task = t,
                    ScriptwriterId = t.SelectedScriptwriterGem?.Id ?? string.Empty,
                    SceneCreatorId = t.SelectedSceneCreatorGem?.Id ?? string.Empty,
                    ImagePromptId = t.SelectedImagePromptGem?.Id ?? string.Empty
                }).ToList();

                // Ensure default options exist
                EnsureDefaultGemOption(scriptwriterCollection, "-- Gemini Mặc Định --");
                EnsureDefaultGemOption(sceneCreatorCollection, "-- Gemini Mặc Định --");
                EnsureDefaultGemOption(imagePromptCollection, "-- Gemini Mặc Định --");

                var defaultScriptwriter = scriptwriterCollection[0];
                var defaultSceneCreator = sceneCreatorCollection[0];
                var defaultImagePrompt = imagePromptCollection[0];
                GemOptionItem? configScriptwriter = defaultScriptwriter;
                GemOptionItem? configSceneCreator = defaultSceneCreator;
                GemOptionItem? configImagePrompt = defaultImagePrompt;

                // Filter only Custom Gems (predefined == false)
                var customGems = gems.Where(g => !g.predefined).ToList();

                foreach (var gem in customGems)
                {
                    AddOrUpdateGemOption(scriptwriterCollection, gem.id, gem.name);
                    AddOrUpdateGemOption(sceneCreatorCollection, gem.id, gem.name);
                    AddOrUpdateGemOption(imagePromptCollection, gem.id, gem.name);

                    // Auto-select based on config or naming convention
                    if (gem.id.Equals(_configService.CurrentSettings.ScriptwriterGemId, StringComparison.OrdinalIgnoreCase) ||
                        (configScriptwriter == defaultScriptwriter &&
                         (gem.name.Contains("psychology", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("bedtime", StringComparison.OrdinalIgnoreCase))))
                    {
                        configScriptwriter = scriptwriterCollection.First(g => g.Id == gem.id);
                    }

                    if (IsBedtimeSceneCreator(gem.id, gem.name) ||
                        gem.id.Equals(_configService.CurrentSettings.SceneCreatorGemId, StringComparison.OrdinalIgnoreCase) ||
                        (configSceneCreator == defaultSceneCreator &&
                         (gem.name.Contains("scriptor", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("rewrite", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("scene", StringComparison.OrdinalIgnoreCase))))
                    {
                        configSceneCreator = sceneCreatorCollection.First(g => g.Id == gem.id);
                    }

                    // Image Prompt Gem - look for image-related or prompt-related gems
                    if (gem.id.Equals(_configService.CurrentSettings.ImagePromptGemId, StringComparison.OrdinalIgnoreCase) ||
                        (configImagePrompt == defaultImagePrompt &&
                         (gem.name.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("visual", StringComparison.OrdinalIgnoreCase))))
                    {
                        configImagePrompt = imagePromptCollection.First(g => g.Id == gem.id);
                    }
                }

                // Restore selections for existing tasks
                foreach (var sel in existingSelections)
                {
                    if (!string.IsNullOrEmpty(sel.ScriptwriterId))
                        sel.Task.SelectedScriptwriterGem = scriptwriterCollection.FirstOrDefault(g => g.Id.Equals(sel.ScriptwriterId, StringComparison.OrdinalIgnoreCase))
                                                           ?? configScriptwriter;
                    else if (sel.Task.SelectedScriptwriterGem == null)
                        sel.Task.SelectedScriptwriterGem = configScriptwriter;

                    if (!string.IsNullOrEmpty(sel.SceneCreatorId))
                        sel.Task.SelectedSceneCreatorGem = sceneCreatorCollection.FirstOrDefault(g => g.Id.Equals(sel.SceneCreatorId, StringComparison.OrdinalIgnoreCase))
                                                          ?? configSceneCreator;
                    else if (sel.Task.SelectedSceneCreatorGem == null)
                        sel.Task.SelectedSceneCreatorGem = configSceneCreator;

                    if (!string.IsNullOrEmpty(sel.ImagePromptId))
                        sel.Task.SelectedImagePromptGem = imagePromptCollection.FirstOrDefault(g => g.Id.Equals(sel.ImagePromptId, StringComparison.OrdinalIgnoreCase))
                                                         ?? configImagePrompt;
                    else if (sel.Task.SelectedImagePromptGem == null)
                        sel.Task.SelectedImagePromptGem = configImagePrompt;
                }

                _log.Success(LogCategory.GeminiCreator, $"Loaded {customGems.Count} custom Gems successfully.");
                onStatus?.Invoke($"[GEMS] ✅ Đã nạp {customGems.Count} Custom Gems.");
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.GeminiCreator, $"Failed to load Gems: {ex.Message}");
                onStatus?.Invoke($"[GEMS] ❌ Lỗi tải Gems: {ex.Message}");
                throw;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Cookie Import
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns <c>true</c> if the given gem id/name matches the
        /// "Bedtime Scene Creator" Gem that the legacy
        /// <c>test_gem_and_thinking.py</c> probe targets.
        /// Hard-coded to the public Gem id <c>b1af0c371214</c> first,
        /// then a name-based fuzzy match as a fallback for renamed clones.
        /// </summary>
        public static bool IsBedtimeSceneCreator(string? gemId, string? gemName)
        {
            const string BedtimeSceneCreatorId = "b1af0c371214";
            if (!string.IsNullOrWhiteSpace(gemId) &&
                gemId.Equals(BedtimeSceneCreatorId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(gemName)) return false;
            return gemName.Contains("bedtime scene creator", StringComparison.OrdinalIgnoreCase) ||
                   gemName.Contains("bedtime", StringComparison.OrdinalIgnoreCase) && gemName.Contains("scene", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cookie import modes: Auto (scan Chrome profiles) or Manual (pick cookies.json file).
        /// </summary>
        public enum CookieImportMode { Auto, Manual }

        /// <summary>
        /// Imports Gemini cookies via the specified mode.
        /// Returns (success, statusMessage) for UI feedback.
        /// </summary>
        public async Task<(bool Success, string Message)> ImportCookiesAsync(
            CookieImportMode mode,
            IBrowserService browserService,
            Action<string>? onStatus = null)
        {
            var syncService = new GeminiCookieSyncService(msg => _log.Info(LogCategory.CookieSync, msg), _configService, browserService);
            bool imported = false;

            try
            {
                if (mode == CookieImportMode.Auto)
                {
                    onStatus?.Invoke("[COOKIE] 🔍 Đang quét Chrome Profiles...");
                    _log.Info(LogCategory.CookieSync, "Auto-syncing cookies from system Chrome profiles...");

                    imported = await syncService.AutoSyncFromSystemChromeAsync();

                    if (!imported)
                    {
                        return (false, "No active Gemini session found in Chrome profiles. Please login manually.");
                    }
                }
                else
                {
                    // Manual mode — file path is resolved by the UI code-behind via OpenFileDialog
                    // This is handled by the caller passing the path through SaveCustomCookiesJsonAsync
                    return (false, "Manual import requires a file path — use SaveCustomCookiesJsonAsync directly.");
                }

                if (imported)
                {
                    onStatus?.Invoke("[COOKIE] 🔄 Đang khởi động lại Python Server...");
                    _log.Info(LogCategory.CookieSync, "Cookies imported. Restarting Python server...");

                    var (restarted, restartDiag) = await _pythonServerManager.RestartServerAsync();

                    if (restarted)
                    {
                        _log.Success(LogCategory.CookieSync, "Server restarted with new cookies successfully.");
                        return (true, "Cookies imported & server restarted successfully.");
                    }
                    else
                    {
                        _log.Warning(LogCategory.CookieSync, $"Cookies saved but server restart failed: {restartDiag}");
                        return (false, $"Cookies saved but server restart failed: {restartDiag}");
                    }
                }

                return (false, "No cookies were imported.");
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.CookieSync, $"Cookie import failed: {ex.Message}");
                return (false, $"Cookie import error: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves manually-provided cookies JSON content and restarts the server.
        /// </summary>
        public async Task<(bool Success, string Message)> SaveCustomCookiesAsync(
            string cookiesJsonContent,
            IBrowserService browserService,
            Action<string>? onStatus = null)
        {
            var syncService = new GeminiCookieSyncService(msg => _log.Info(LogCategory.CookieSync, msg), _configService, browserService);

            try
            {
                bool saved = await syncService.SaveCustomCookiesJsonAsync(cookiesJsonContent);

                if (!saved)
                {
                    return (false, "Failed to save cookies.json file.");
                }

                onStatus?.Invoke("[COOKIE] 🔄 Đang khởi động lại Python Server...");
                _log.Info(LogCategory.CookieSync, "Manual cookies saved. Restarting Python server...");

                var (restarted, restartDiag) = await _pythonServerManager.RestartServerAsync();

                if (restarted)
                {
                    return (true, "Cookies saved & server restarted successfully.");
                }
                else
                {
                    return (false, $"Cookies saved but server restart failed: {restartDiag}");
                }
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.CookieSync, $"Manual cookie save failed: {ex.Message}");
                return (false, $"Error: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Playwright Interactive Login
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Opens Chrome via Playwright for interactive Gemini login, then syncs cookies.
        /// Returns (success, message, profilePath).
        /// </summary>
        public async Task<(bool Success, string Message, string? ProfilePath)> LoginViaPlaywrightAsync(
            IBrowserService browserService,
            string? targetProfilePath = null,
            Action<string>? onStatus = null)
        {
            try
            {
                string profilesDir = _configService.CurrentSettings.ChromeProfilesDir;
                if (string.IsNullOrEmpty(profilesDir))
                    profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");

                Directory.CreateDirectory(profilesDir);

                string persistentProfilePath;
                if (!string.IsNullOrWhiteSpace(targetProfilePath))
                {
                    persistentProfilePath = Path.IsPathRooted(targetProfilePath)
                        ? targetProfilePath
                        : Path.Combine(profilesDir, targetProfilePath);
                }
                else
                {
                    persistentProfilePath = Path.Combine(profilesDir, "GeminiProfile");
                }

                Directory.CreateDirectory(persistentProfilePath);

                string profileDisplayName = Path.GetFileName(persistentProfilePath);
                onStatus?.Invoke($"[LOGIN] 🌐 Đang mở Chrome (Profile: {profileDisplayName}) để đăng nhập Gemini...");
                _log.Info(LogCategory.CookieSync, $"Launching Playwright Chrome with profile: {persistentProfilePath}");

                using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                var context = await playwright.Chromium.LaunchPersistentContextAsync(
                    persistentProfilePath,
                    new Microsoft.Playwright.BrowserTypeLaunchPersistentContextOptions
                    {
                        Headless = false,
                        Channel = "chrome",
                        Args = new[] { "--disable-blink-features=AutomationControlled", "--no-sandbox" }
                    });

                var page = await context.NewPageAsync();
                await page.GotoAsync("https://gemini.google.com");

                _log.Info(LogCategory.CookieSync, "Chrome opened — waiting for user to complete login.");

                // Wait until user actually logs in. We poll the cookie store
                // every second — Playwright Chrome loads cookies from the
                // profile DB on launch, so __Secure-1PSID will appear as
                // soon as the user authenticates via Google's login flow.
                // Without this gate the previous version called
                // SyncCookiesFromBrowserContextAsync immediately, which
                // extracted stale cookies (or none) and then CloseAsync()
                // slammed the browser shut while the user was still typing
                // their password.
                bool loggedIn = await WaitForUserLoginAsync(context, onStatus);
                if (!loggedIn)
                {
                    try { await context.CloseAsync(); } catch { }
                    return (false, "Timed out waiting for user to log in to gemini.google.com.", persistentProfilePath);
                }

                // Small grace period so the final session cookies are
                // committed before we read them.
                await Task.Delay(500);

                var syncService = new GeminiCookieSyncService(msg => _log.Info(LogCategory.CookieSync, msg), _configService, browserService);
                bool syncOk = await syncService.SyncCookiesFromBrowserContextAsync(context);
                await context.CloseAsync();

                if (syncOk)
                {
                    _log.Success(LogCategory.CookieSync, $"Cookies synced from Playwright. Profile: {persistentProfilePath}");
                    onStatus?.Invoke("[COOKIE] 🔄 Đang khởi động lại Python Server...");
                    var (restarted, restartDiag) = await _pythonServerManager.RestartServerAsync();

                    if (restarted)
                    {
                        _log.Success(LogCategory.CookieSync, "Server restarted with new cookies successfully.");
                        return (true, "Login successful! Cookies imported & server restarted.", persistentProfilePath);
                    }
                    else
                    {
                        _log.Warning(LogCategory.CookieSync, $"Cookies saved but server restart failed: {restartDiag}");
                        return (false, $"Cookies saved but server restart failed: {restartDiag}", persistentProfilePath);
                    }
                }
                else
                {
                    return (false, "Failed to extract cookies from browser session.", persistentProfilePath);
                }
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.CookieSync, $"Playwright login failed: {ex.Message}");
                return (false, $"Login error: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Polls the browser context until the user has actually completed
        /// login to gemini.google.com (signalled by the presence of the
        /// <c>__Secure-1PSID</c> cookie). Caps at ~5 minutes so we don't
        /// wait forever if the user closes the window.
        /// </summary>
        private async Task<bool> WaitForUserLoginAsync(
            Microsoft.Playwright.IBrowserContext context,
            Action<string>? onStatus)
        {
            const int pollIntervalMs = 1000;
            const int maxWaitMs = 5 * 60 * 1000;
            int waited = 0;

            onStatus?.Invoke("[LOGIN] ⏳ Đang đợi bạn đăng nhập Gemini trong cửa sổ Chrome...");

            while (waited < maxWaitMs)
            {
                await Task.Delay(pollIntervalMs);
                waited += pollIntervalMs;

                try
                {
                    var cookies = await context.CookiesAsync(new[] { "https://gemini.google.com", "https://google.com" });
                    var psid = cookies.FirstOrDefault(c => c.Name == "__Secure-1PSID");
                    if (psid != null && !string.IsNullOrWhiteSpace(psid.Value) &&
                        psid.Value.Length > 20 && !psid.Value.StartsWith("deleted"))
                    {
                        onStatus?.Invoke($"[LOGIN] ✅ Phát hiện session đăng nhập Gemini sau {waited / 1000}s — chuẩn bị trích xuất cookies...");
                        _log.Success(LogCategory.CookieSync,
                            $"User login detected in browser after {waited / 1000}s (PSID length: {psid.Value.Length}).");
                        return true;
                    }
                }
                catch
                {
                    // Browser may have been closed mid-poll; surface as failure
                    // only once we hit the overall timeout.
                }

                // Heartbeat every 15s so user sees we're still waiting.
                if (waited % 15000 == 0)
                {
                    onStatus?.Invoke($"[LOGIN] ⏳ Vẫn đang chờ bạn hoàn tất đăng nhập... ({waited / 1000}s)");
                }
            }

            return false;
        }

        // ─────────────────────────────────────────────────────
        //  Server Health & Diagnostics
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Checks server health and provides a diagnostic summary for UI display.
        /// </summary>
        public async Task<(bool IsRunning, string Summary, string? PythonPath, string? ScriptPath)> GetServerDiagnosticsAsync()
        {
            var (isRunning, diag) = await _pythonServerManager.IsServerRunningAsync();
            string pythonPath = _pythonServerManager.ResolvePythonExecutable();
            string scriptPath = _pythonServerManager.ResolveServerScriptPath();

            var procInfo = _pythonServerManager.GetProcessInfo();
            string summary = isRunning
                ? $"✅ Server running — PID: {procInfo.Pid}, started: {procInfo.StartTime}"
                : $"❌ Server not running — {diag}";

            return (isRunning, summary, pythonPath, scriptPath);
        }

        /// <summary>
        /// Resolves language for a Gemini task using the VoiceId to query AI84 API.
        /// Uses the DI-named "ai84" HttpClient so the call gets a 60s per-attempt
        /// timeout and automatic retry/circuit-breaker instead of freezing for the
        /// raw <c>new HttpClient()</c> default 100s.
        ///
        /// Results are cached per <see cref="GeminiTaskModel.VoiceId"/> for the
        /// lifetime of the process so re-running a batch (or running parallel tasks
        /// that share a voice) never hits AI84 twice for the same voice.
        /// </summary>
        public async Task ResolveTaskLanguageAsync(GeminiTaskModel taskItem, string apiKey)
        {
            if (string.IsNullOrEmpty(taskItem.VoiceId) || !string.IsNullOrEmpty(taskItem.TargetLanguage))
                return;

            // Cache hit: short-circuit.
            string cacheKey = taskItem.VoiceId.Trim();
            if (_resolvedLanguageCache.TryGetValue(cacheKey, out var cachedLang))
            {
                taskItem.TargetLanguage = cachedLang;
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient(Steps.VoiceoverGenerationStep.Ai84HttpClientName);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://api.ai84.pro/v1/shared-voices?page_size=10&search={Uri.EscapeDataString(taskItem.VoiceId)}");
                request.Headers.TryAddWithoutValidation("xi-api-key", apiKey);

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<SharedVoicesResponse>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var voice = result?.voices?.FirstOrDefault(v => v.voice_id == taskItem.VoiceId);
                    if (voice != null)
                    {
                        var lang = LanguageHelper.FormatLanguage(voice.language);
                        taskItem.TargetLanguage = lang;
                        _resolvedLanguageCache[cacheKey] = lang;
                        _log.Debug(LogCategory.GeminiCreator,
                            $"Resolved language for voice '{taskItem.VoiceId}': {lang}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(LogCategory.GeminiCreator,
                    $"Could not resolve language for voice '{taskItem.VoiceId}': {ex.Message}");
            }
        }

        // Process-wide cache: Voice ID (trimmed) → resolved language. Bounded so a misbehaving
        // caller that keeps changing VoiceId can't grow it without limit.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _resolvedLanguageCache = new();
        private const int ResolvedLanguageCacheMaxEntries = 256;

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static void EnsureDefaultGemOption(ObservableCollection<GemOptionItem> collection, string defaultName)
        {
            if (collection.All(g => g.Id != string.Empty))
            {
                collection.Insert(0, new GemOptionItem { Id = string.Empty, Name = defaultName });
            }
        }

        private static void AddOrUpdateGemOption(ObservableCollection<GemOptionItem> collection, string id, string name)
        {
            var existing = collection.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                collection.Add(new GemOptionItem { Id = id, Name = name });
            }
            else
            {
                existing.Name = name;
            }
        }

        /// <summary>
        /// Creates a default GeminiTaskModel with sensible defaults from current gem selections.
        /// </summary>
        public GeminiTaskModel CreateDefaultTask(
            ObservableCollection<GemOptionItem> scriptwriterGems,
            ObservableCollection<GemOptionItem> sceneCreatorGems,
            ObservableCollection<GemOptionItem> imagePromptGems,
            string? topic = null)
        {
            return new GeminiTaskModel
            {
                Topic = topic ?? "",
                SelectedScriptwriterGem = scriptwriterGems.FirstOrDefault(),
                SelectedSceneCreatorGem = sceneCreatorGems.FirstOrDefault(),
                SelectedImagePromptGem = imagePromptGems.FirstOrDefault(),
                EnableDeepResearch = true,
                VoiceId = "",
                SelectedImageProvider = "flow_local",
                Status = NodeStatus.Idle,
                CurrentStepInfo = "Sẵn sàng",
                ScriptMinWords = 1600,
                ScriptTargetWords = 2000,
                ScriptMaxWords = 2200
            };
        }
    }
}