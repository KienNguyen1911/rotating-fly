using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    /// Handles: gem loading, cookie import (text paste + persistent profile refresh), server lifecycle.
    /// All UI concerns (MessageBox, button states, status bar) remain in the code-behind
    /// via callback delegates — this service is pure logic, testable without WPF.
    /// </summary>
    public class GeminiCreatorService
    {
        private readonly ILogService _log;
        private readonly IConfigService _configService;
        private readonly GeminiApiService _geminiApiService;
        private readonly PythonServerManager _pythonServerManager;

        public GeminiCreatorService(
            ILogService logService,
            IConfigService configService,
            GeminiApiService geminiApiService,
            PythonServerManager pythonServerManager)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _geminiApiService = geminiApiService ?? throw new ArgumentNullException(nameof(geminiApiService));
            _pythonServerManager = pythonServerManager ?? throw new ArgumentNullException(nameof(pythonServerManager));
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
            ObservableCollection<GeminiTaskModel> taskCollection,
            Action<string>? onStatus = null)
        {
            try
            {
                onStatus?.Invoke("[GEMS] 🔄 Đang tải danh sách Gemini Gems từ API...");
                _log.Info(LogCategory.GeminiCreator, "Loading Gemini Gems from API...");

                var gems = await _geminiApiService.GetGemsAsync(includeHidden: true);

                var existingSelections = taskCollection.Select(t => new
                {
                    Task = t,
                    ScriptwriterId = t.SelectedScriptwriterGem?.Id ?? string.Empty,
                    SceneCreatorId = t.SelectedSceneCreatorGem?.Id ?? string.Empty
                }).ToList();

                EnsureDefaultGemOption(scriptwriterCollection, "-- Gemini Mặc Định --");
                EnsureDefaultGemOption(sceneCreatorCollection, "-- Gemini Mặc Định --");

                var defaultScriptwriter = scriptwriterCollection[0];
                var defaultSceneCreator = sceneCreatorCollection[0];
                GemOptionItem? configScriptwriter = defaultScriptwriter;
                GemOptionItem? configSceneCreator = defaultSceneCreator;

                var customGems = gems.Where(g => !g.predefined).ToList();

                foreach (var gem in customGems)
                {
                    AddOrUpdateGemOption(scriptwriterCollection, gem.id, gem.name);
                    AddOrUpdateGemOption(sceneCreatorCollection, gem.id, gem.name);

                    if (gem.id.Equals(_configService.CurrentSettings.ScriptwriterGemId, StringComparison.OrdinalIgnoreCase) ||
                        (configScriptwriter == defaultScriptwriter &&
                         (gem.name.Contains("psychology", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("bedtime", StringComparison.OrdinalIgnoreCase))))
                    {
                        configScriptwriter = scriptwriterCollection.First(g => g.Id == gem.id);
                    }

                    if (gem.id.Equals(_configService.CurrentSettings.SceneCreatorGemId, StringComparison.OrdinalIgnoreCase) ||
                        (configSceneCreator == defaultSceneCreator &&
                         (gem.name.Contains("scriptor", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("rewrite", StringComparison.OrdinalIgnoreCase) ||
                          gem.name.Contains("scene", StringComparison.OrdinalIgnoreCase))))
                    {
                        configSceneCreator = sceneCreatorCollection.First(g => g.Id == gem.id);
                    }
                }

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
        //  Cookie Import — 2-phase flow
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// True if a Chrome profile has been saved (i.e. user has already imported cookies at least once).
        /// Used by the UI to decide whether to auto-refresh or to open the input popup.
        /// </summary>
        public bool HasSavedChromeProfile()
        {
            var syncService = new GeminiCookieSyncService(
                msg => _log.Info(LogCategory.CookieSync, msg),
                _configService);
            return syncService.HasSavedProfile();
        }

        /// <summary>
        /// Phase 1 — User pastes cookies (any of the supported formats). Saves cookies.json,
        /// opens the persisted Chrome profile at the configured path, injects cookies,
        /// navigates to gemini.google.com, then waits for the user to confirm in a WPF dialog.
        /// The user can complete manual login (2FA/CAPTCHA) in the open Chrome window if Google
        /// rejects the pasted cookies.
        /// </summary>
        public async Task<(bool Success, string Message)> ImportCookiesFromTextAsync(
            string rawCookieText,
            System.Windows.Window? parentWindow = null,
            Action<string>? onStatus = null)
        {
            var syncService = new GeminiCookieSyncService(
                msg => _log.Info(LogCategory.CookieSync, msg),
                _configService);

            try
            {
                var (ok, msg, _) = await syncService.ImportCookiesFromTextAsync(
                    rawCookieText,
                    awaitUserConfirmation: true,
                    parentWindow: parentWindow);
                if (!ok)
                {
                    return (false, msg);
                }

                onStatus?.Invoke("[COOKIE] 🔄 Đang khởi động lại Python Server...");
                _log.Info(LogCategory.CookieSync, "Cookies imported. Restarting Python server...");

                var (restarted, restartDiag) = await _pythonServerManager.RestartServerAsync();
                if (restarted)
                {
                    _log.Success(LogCategory.CookieSync, "Server restarted with new cookies successfully.");
                    return (true, $"{msg} Server đã khởi động lại.");
                }

                _log.Warning(LogCategory.CookieSync, $"Cookies saved but server restart failed: {restartDiag}");
                return (false, $"{msg} Nhưng server restart thất bại: {restartDiag}");
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.CookieSync, $"Cookie import failed: {ex.Message}");
                return (false, $"Lỗi: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase 2 — Open the saved Chrome profile, extract cookies via Playwright,
        /// write cookies.json, restart the Python server. No user input required.
        /// If the saved profile's session is expired, returns a friendly hint to re-import.
        /// </summary>
        public async Task<(bool Success, string Message, bool SessionExpired)> RefreshCookiesFromSavedProfileAsync(
            Action<string>? onStatus = null)
        {
            var syncService = new GeminiCookieSyncService(
                msg => _log.Info(LogCategory.CookieSync, msg),
                _configService);

            try
            {
                if (!syncService.HasSavedProfile())
                {
                    return (false, "Chưa có Chrome profile được lưu. Vui lòng bấm 'Nhập Cookies' để tạo profile trước.", false);
                }

                var (ok, msg, expired) = await syncService.RefreshCookiesFromSavedProfileAsync();
                if (!ok)
                {
                    return (false, msg, expired);
                }

                onStatus?.Invoke("[COOKIE] 🔄 Đang khởi động lại Python Server...");
                _log.Info(LogCategory.CookieSync, "Cookies refreshed. Restarting Python server...");

                var (restarted, restartDiag) = await _pythonServerManager.RestartServerAsync();
                if (restarted)
                {
                    _log.Success(LogCategory.CookieSync, "Server restarted with refreshed cookies.");
                    return (true, $"{msg} Server đã khởi động lại.", false);
                }

                _log.Warning(LogCategory.CookieSync, $"Cookies refreshed but server restart failed: {restartDiag}");
                return (false, $"{msg} Server restart thất bại: {restartDiag}", false);
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.CookieSync, $"Cookie refresh failed: {ex.Message}");
                return (false, $"Lỗi: {ex.Message}", false);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Server Health & Diagnostics
        // ─────────────────────────────────────────────────────

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

        public async Task ResolveTaskLanguageAsync(GeminiTaskModel taskItem, string apiKey)
        {
            if (string.IsNullOrEmpty(taskItem.VoiceId) || !string.IsNullOrEmpty(taskItem.TargetLanguage))
                return;

            try
            {
                using var client = new System.Net.Http.HttpClient();
                var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get,
                    $"https://api.ai84.pro/v1/shared-voices?page_size=10&search={Uri.EscapeDataString(taskItem.VoiceId)}");
                request.Headers.Add("xi-api-key", apiKey);

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<SharedVoicesResponse>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    var voice = result?.voices?.FirstOrDefault(v => v.voice_id == taskItem.VoiceId);
                    if (voice != null)
                    {
                        taskItem.TargetLanguage = LanguageHelper.FormatLanguage(voice.language);
                        _log.Debug(LogCategory.GeminiCreator,
                            $"Resolved language for voice '{taskItem.VoiceId}': {taskItem.TargetLanguage}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(LogCategory.GeminiCreator,
                    $"Could not resolve language for voice '{taskItem.VoiceId}': {ex.Message}");
            }
        }

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
            string? topic = null)
        {
            return new GeminiTaskModel
            {
                Topic = topic ?? "",
                SelectedScriptwriterGem = scriptwriterGems.FirstOrDefault(),
                SelectedSceneCreatorGem = sceneCreatorGems.FirstOrDefault(),
                EnableDeepResearch = true,
                VoiceId = "",
                SelectedImageProvider = "flow_local",
                Status = NodeStatus.Idle,
                CurrentStepInfo = "Sẵn sàng"
            };
        }
    }
}
