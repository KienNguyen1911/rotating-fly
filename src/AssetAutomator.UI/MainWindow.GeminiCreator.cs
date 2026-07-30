using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AssetAutomator.Infrastructure.Helpers;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.UI.Models.Nodes;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Steps;
using AssetAutomator.Infrastructure.Logging;

namespace AssetAutomator.UI
{
    public partial class MainWindow : Window
    {
        // ── DI-injected services ──
        private ILogService? _logService;
        private GeminiApiService? _geminiApiService;
        private GeminiVideoPipelineService? _geminiVideoPipelineService;
        private PipelineOrchestrator? _pipelineOrchestrator;
        private GeminiCreatorService? _geminiCreatorService;
        private PythonServerManager? _pythonServerManager;

        private bool _isGeminiOperationBusy;
        private System.Windows.Threading.DispatcherTimer? _geminiStatusTimer;

        public ObservableCollection<GeminiTaskModel> GeminiTasks { get; } = new();
        public ObservableCollection<GemOptionItem> AvailableScriptwriterGems { get; } = new();
        public ObservableCollection<GemOptionItem> AvailableSceneCreatorGems { get; } = new();
        public ObservableCollection<string> AvailableImageProviders { get; } = new() { "flow_local", "glabs" };

        /// <summary>
        /// All 9 Gemini models from constants.py Model enum.
        /// Grouped: Flash (standard), Pro (thinking), Flash-Thinking, Plus/Advanced variants.
        /// </summary>
        public ObservableCollection<string> AvailableAiModels { get; } = new()
        {
            // ── Standard (Basic) ──
            "gemini-3-flash",
            "gemini-3-pro",               // 🧠 Pro = thinking
            "gemini-3-flash-thinking",
            // ── Plus (capacity=4) ──
            "gemini-3-flash-plus",
            "gemini-3-pro-plus",          // 🧠
            "gemini-3-flash-thinking-plus",
            // ── Advanced (capacity=2) ──
            "gemini-3-flash-advanced",
            "gemini-3-pro-advanced",      // 🧠
            "gemini-3-flash-thinking-advanced",
        };

        // ─────────────────────────────────────────────────────
        //  Service Initialization (DI)
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Initializes all Gemini-related services with proper dependency injection.
        /// Called lazily on first use. Thread-safe via _geminiApiService null check.
        /// </summary>
        private void InitializeGeminiCreatorServices()
        {
            if (_logService != null) return; // Already initialized

            // 1. Centralized logging (shared across all services)
            _logService = new LogService();

            // 2. Subscribe to log entries for UI updates
            _logService.OnLogEntry += entry =>
            {
                Dispatcher.Invoke(() =>
                {
                    // Route to main log and Python server log panel
                    AppendToMainLog(entry);
                    AppendToPythonServerLog(entry);
                });
            };

            // 3. Python server manager (uses log service internally)
            _pythonServerManager = new PythonServerManager(_logService, ConfigService.Instance);

            // 4. Gemini API service
            _geminiApiService = new GeminiApiService(ConfigService.Instance);

            // 5. Gemini Creator business logic service
            _geminiCreatorService = new GeminiCreatorService(_logService, ConfigService.Instance, _geminiApiService, _pythonServerManager);

            // 6. Lazy-initialize step services for Gemini Pipeline (single-task)
            if (_geminiVideoPipelineService == null)
            {
                var batchImageGenService = new BatchImageGenService();
                var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);
                var voiceoverStep = new VoiceoverGenerationStep(ConfigService.Instance);
                var sceneBreakdownStep = new GeminiPlaywrightSceneBreakdownStep(ConfigService.Instance);
                var imageBatchStep = new SceneImageBatchStep(batchImageGenService, ConfigService.Instance);

                _geminiVideoPipelineService = new GeminiVideoPipelineService(
                    ConfigService.Instance,
                    topicResearchStep,
                    voiceoverStep,
                    sceneBreakdownStep,
                    imageBatchStep
                );
            }

            // 7. Lazy-initialize PipelineOrchestrator (multi-task batch)
            var batchImageGenServiceForOrchestrator = new BatchImageGenService();
            var topicResearchStepForOrchestrator = new GeminiTopicResearchStep(_geminiApiService);
            var voiceoverStepForOrchestrator = new VoiceoverGenerationStep(ConfigService.Instance);
            var sceneBreakdownStepForOrchestrator = new GeminiPlaywrightSceneBreakdownStep(ConfigService.Instance);

            _pipelineOrchestrator ??= new PipelineOrchestrator(
                geminiApiService: _geminiApiService,
                configService: ConfigService.Instance,
                voiceoverStep: voiceoverStepForOrchestrator,
                sceneBreakdownStep: sceneBreakdownStepForOrchestrator,
                batchImageGenService: batchImageGenServiceForOrchestrator,
                topicResearchStep: topicResearchStepForOrchestrator,
                maxDeepResearch: 2,
                maxVoiceover: 3,
                maxSceneCreator: 4,
                maxImageGen: 1
            );

            // 8. Add default initial task if empty
            if (GeminiTasks.Count == 0)
            {
                GeminiTasks.Add(_geminiCreatorService!.CreateDefaultTask(AvailableScriptwriterGems, AvailableSceneCreatorGems));
            }

            // 9. Load gems ONCE on initial setup if not yet loaded
            if (AvailableScriptwriterGems.Count == 0)
            {
                _ = LoadGeminiGemsToComboboxesAsync();
            }

            _logService.Info(LogCategory.GeminiCreator, "Gemini Creator services initialized successfully.");
        }

        // ─────────────────────────────────────────────────────
        //  Log Routing (ILogService → UI)
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Routes a structured log entry to the main application log TextBox.
        /// </summary>
        private void AppendToMainLog(LogEntry entry)
        {
            // Route only relevant categories to main log to avoid noise
            if (entry.Category is LogCategory.GeminiCreator or LogCategory.GeminiApi
                or LogCategory.Pipeline or LogCategory.CookieSync
                or LogCategory.ImageGen or LogCategory.Voiceover)
            {
                Log($"[{entry.CategoryLabel.Trim('[', ']')}] {entry.LevelIcon} {entry.Message}");
            }
        }

        /// <summary>
        /// Routes Python server log entries to the dedicated Python server log panel.
        /// </summary>
        private void AppendToPythonServerLog(LogEntry entry)
        {
            if (entry.Category != LogCategory.PythonServer) return;

            Dispatcher.Invoke(() =>
            {
                if (TxtPythonServerLog == null) return;

                string line = $"{entry.FormattedTimestamp} {entry.LevelIcon} {entry.Message}";
                TxtPythonServerLog.AppendText(line + Environment.NewLine);
                TxtPythonServerLog.ScrollToEnd();

                // Update the server status indicator
                UpdatePythonServerStatusIndicator(entry);
            });
        }

        /// <summary>
        /// Updates the Python server status indicator based on log entries.
        /// </summary>
        private void UpdatePythonServerStatusIndicator(LogEntry entry)
        {
            if (entry.Message.Contains("successfully launched", StringComparison.OrdinalIgnoreCase) ||
                entry.Message.Contains("Health check OK", StringComparison.OrdinalIgnoreCase))
            {
                if (TxtPythonServerStatus != null)
                {
                    TxtPythonServerStatus.Text = "✅ Running";
                    TxtPythonServerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
            }
            else if (entry.Level == LogLevel.Error &&
                     (entry.Message.Contains("exited prematurely", StringComparison.OrdinalIgnoreCase) ||
                      entry.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
            {
                if (TxtPythonServerStatus != null)
                {
                    TxtPythonServerStatus.Text = "❌ Crashed";
                    TxtPythonServerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            }
            else if (entry.Message.Contains("Server successfully launched", StringComparison.OrdinalIgnoreCase) ||
                     entry.Message.Contains("responding", StringComparison.OrdinalIgnoreCase))
            {
                if (TxtPythonServerStatus != null)
                {
                    TxtPythonServerStatus.Text = "✅ Running";
                    TxtPythonServerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Gem Loading
        // ─────────────────────────────────────────────────────

        private async Task LoadGeminiGemsToComboboxesAsync()
        {
            try
            {
                _geminiApiService ??= new GeminiApiService(ConfigService.Instance);

                await _geminiCreatorService!.LoadGeminiGemsAsync(
                    AvailableScriptwriterGems,
                    AvailableSceneCreatorGems,
                    GeminiTasks,
                    onStatus: msg => Dispatcher.Invoke(() => SetGeminiStatus("🔄", msg, "#3B82F6"))
                );

                Dispatcher.Invoke(() =>
                {
                    Log($"[SUCCESS] Đã làm mới danh sách Gems!");
                    SetGeminiStatus("✅", "Tải danh sách Gems thành công!", "#10B981");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    Log($"[ERROR] Không thể tải danh sách Gemini Gems: {ex.Message}");
                    SetGeminiStatus("❌", $"Lỗi tải Gems: {ex.Message}", "#EF4444");
                });
            }
        }

        private async void BtnRefreshGems_Click(object sender, RoutedEventArgs e)
        {
            if (_isGeminiOperationBusy)
            {
                SetGeminiStatus("⏳", "Đang thực hiện thao tác khác, vui lòng đợi...", "#F59E0B");
                return;
            }

            _isGeminiOperationBusy = true;
            SetButtonLoading(BtnRefreshGems, "⏳ Đang tải danh sách Gems...");
            InitializeGeminiCreatorServices();

            try
            {
                Log("[INFO] Đang tải lại danh sách Gemini Gems...");
                SetGeminiStatus("🔄", "Đang kết nối tới Gemini API để tải danh sách Gems...", "#3B82F6");

                await LoadGeminiGemsToComboboxesAsync();

                SetGeminiStatus("✅", "Đã tải danh sách Gems thành công!", "#10B981");
                ShowGeminiNotification("✅ Tải Danh Sách Gems thành công!", "#10B981");
            }
            catch (Exception ex)
            {
                SetGeminiStatus("❌", $"Lỗi tải Gems: {ex.Message}", "#EF4444");
                ShowGeminiNotification($"❌ Lỗi tải Gems: {ex.Message}", "#EF4444");
            }
            finally
            {
                ResetButtonNormal(BtnRefreshGems, "🔄 Tải Danh Sách Gems");
                _isGeminiOperationBusy = false;
            }
        }

        private async void BtnImportCookies_Click(object sender, RoutedEventArgs e)
        {
            if (_isGeminiOperationBusy)
            {
                SetGeminiStatus("⏳", "Đang thực hiện thao tác khác, vui lòng đợi...", "#F59E0B");
                return;
            }

            var choice = MessageBox.Show(
                "Bạn muốn nạp Gemini Cookies theo phương thức nào?\n\n" +
                "• Bấm YES: Tự động trích xuất Cookies từ các Profile Chrome sẵn có trên hệ thống.\n" +
                "• Bấm NO: Chọn file cookies.json (hoặc văn bản cookie) để nạp thủ công.\n" +
                "• Bấm CANCEL: Hủy bỏ.",
                "🔑 Nạp / Import Cookies Gemini",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question
            );

            if (choice == MessageBoxResult.Cancel) return;

            _isGeminiOperationBusy = true;
            SetButtonLoading(BtnImportCookies, "⏳ Đang nạp Cookies...");
            InitializeGeminiCreatorServices();

            try
            {
                if (choice == MessageBoxResult.Yes)
                {
                    // ── Auto-sync from system Chrome profiles ──
                    Log("[COOKIE-IMPORT] ⏳ Đang tự động quét & trích xuất Cookies Gemini từ các Chrome Profiles...");
                    SetGeminiStatus("🔍", "Đang quét Chrome Profiles để tìm Cookies Gemini...", "#3B82F6");

                    var (success, msg) = await _geminiCreatorService!.ImportCookiesAsync(
                        GeminiCreatorService.CookieImportMode.Auto,
                        _browserService,
                        onStatus: status => Dispatcher.Invoke(() => SetGeminiStatus("🔍", status, "#3B82F6"))
                    );

                    if (success)
                    {
                        await HandleCookieImportSuccessAsync(msg);
                    }
                    else
                    {
                        // Fallback: Open interactive browser for user to login
                        await HandleCookieImportFallbackAsync(msg);
                    }
                }
                else if (choice == MessageBoxResult.No)
                {
                    // ── Manual file import ──
                    await HandleManualCookieImportAsync();
                }
            }
            catch (Exception ex)
            {
                SetGeminiStatus("❌", $"Lỗi nạp Cookies: {ex.Message}", "#EF4444");
                ShowGeminiNotification($"❌ Lỗi: {ex.Message}", "#EF4444");
            }
            finally
            {
                ResetButtonNormal(BtnImportCookies, "🔑 Nạp Cookies Gemini");
                _isGeminiOperationBusy = false;
            }
        }

        /// <summary>
        /// Handles successful cookie import — restarts server & reloads gems.
        /// </summary>
        private async Task HandleCookieImportSuccessAsync(string msg)
        {
            Log($"[COOKIE-IMPORT] 🎉 {msg}");
            SetGeminiStatus("✅", "Cookies đã được nạp & Server đã khởi động lại!", "#10B981");
            ShowGeminiNotification("🔑 Nạp Cookies Gemini thành công! Server đã sẵn sàng.", "#10B981");

            // Reload gems list
            SetGeminiStatus("🔄", "Đang làm mới danh sách Gems...", "#3B82F6");
            await LoadGeminiGemsToComboboxesAsync();
            SetGeminiStatus("✅", "Tất cả đã sẵn sàng! Cookies + Gems đã được cập nhật.", "#10B981");
        }

        /// <summary>
        /// Fallback when auto cookie detection fails — offers Playwright interactive login.
        /// </summary>
        private async Task HandleCookieImportFallbackAsync(string msg)
        {
            Log($"[COOKIE-IMPORT] ⚠️ {msg}");

            var confirmLogin = MessageBox.Show(
                "Chưa tìm thấy phiên đăng nhập Gemini sẵn có trong các Profile Chrome.\n\n" +
                "Bạn có muốn mở Chrome để đăng nhập https://gemini.google.com ngay bây giờ không?",
                "Yêu cầu Đăng nhập Gemini",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information
            );

            if (confirmLogin != MessageBoxResult.Yes) return;

            Log("[COOKIE-IMPORT] 🌐 Đang mở Chrome để bạn hoàn tất đăng nhập Gemini...");
            SetGeminiStatus("🌐", "Đang mở Chrome — vui lòng đăng nhập Gemini trên trình duyệt...", "#3B82F6");

            var (loginOk, loginMsg, profilePath) = await _geminiCreatorService!.LoginViaPlaywrightAsync(
                _browserService,
                onStatus: status => Dispatcher.Invoke(() => SetGeminiStatus("🌐", status, "#3B82F6"))
            );

            if (loginOk)
            {
                if (profilePath != null)
                    Log($"[COOKIE-IMPORT] 💾 Đã lưu Chrome profile Gemini vào: {profilePath}");

                Dispatcher.Invoke(() => LoadProfiles());

                var (restartOk, restartMsg) = await _pythonServerManager!.RestartServerAsync();
                if (restartOk)
                {
                    await HandleCookieImportSuccessAsync(loginMsg);
                }
                else
                {
                    SetGeminiStatus("⚠️", $"Cookies saved but server restart failed: {restartMsg}", "#F59E0B");
                    ShowGeminiNotification("⚠️ Đã lưu cookies. Vui lòng kiểm tra lại Python Server.", "#F59E0B");
                }
            }
            else
            {
                Log($"[COOKIE-IMPORT] ❌ {loginMsg}");
                SetGeminiStatus("❌", loginMsg, "#EF4444");
            }
        }

        /// <summary>
        /// Handles manual cookie file import via OpenFileDialog.
        /// </summary>
        private async Task HandleManualCookieImportAsync()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Chọn file cookies.json Gemini"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SetGeminiStatus("📂", "Đang đọc file cookies.json...", "#3B82F6");

                try
                {
                    string content = await File.ReadAllTextAsync(openFileDialog.FileName);
                    var (success, msg) = await _geminiCreatorService!.SaveCustomCookiesAsync(
                        content,
                        _browserService,
                        onStatus: status => Dispatcher.Invoke(() => SetGeminiStatus("📂", status, "#3B82F6"))
                    );

                    if (success)
                    {
                        await HandleCookieImportSuccessAsync(msg);
                    }
                    else
                    {
                        SetGeminiStatus("⚠️", msg, "#F59E0B");
                        ShowGeminiNotification($"⚠️ {msg}", "#F59E0B");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[COOKIE-IMPORT] ❌ Lỗi khi đọc file cookies: {ex.Message}");
                    MessageBox.Show($"Lỗi khi đọc file cookies: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Step 1: Suggests YouTube video topics based on a channel URL entered in the selected task's Topic field.
        /// </summary>
        private async void BtnSuggestTopics_Click(object sender, RoutedEventArgs e)
        {
            var selectedTasks = GeminiTasks.Where(t => t.IsSelected).ToList();
            if (selectedTasks.Count == 0)
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất 1 task và nhập link kênh YouTube vào ô 'Chủ Đề / Link YouTube'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var taskItem = selectedTasks[0];
            string channelUrl = taskItem.Topic.Trim();

            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                MessageBox.Show("Vui lòng nhập link kênh YouTube vào ô 'Chủ Đề / Link YouTube' trước khi gợi ý chủ đề.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isGeminiOperationBusy)
            {
                SetGeminiStatus("⏳", "Đang thực hiện thao tác khác, vui lòng đợi...", "#F59E0B");
                return;
            }

            _isGeminiOperationBusy = true;
            SetButtonLoading(BtnSuggestTopics, "⏳ Đang phân tích kênh YouTube...");
            InitializeGeminiCreatorServices();

            try
            {
                Log($"[TOPIC-SUGGEST] 🔍 Đang phân tích kênh YouTube: '{channelUrl}'...");
                SetGeminiStatus("🔍", $"Đang phân tích kênh YouTube và gợi ý chủ đề...", "#3B82F6");

                var suggestionStep = new YoutubeTopicSuggestionStep(_geminiApiService ?? throw new InvalidOperationException("GeminiApiService not initialized"));
                string? scriptwriterGemId = taskItem.SelectedScriptwriterGem?.Id;
                string model = taskItem.ScriptwriterModel ?? "gemini-3-flash";

                var suggestions = await Task.Run(() =>
                    suggestionStep.SuggestTopicsAsync(
                        channelUrl: channelUrl,
                        scriptwriterGemId: scriptwriterGemId,
                        model: model,
                        logAction: msg => Dispatcher.Invoke(() => Log(msg))
                    ));

                if (suggestions == null || suggestions.SuggestedTopics.Count == 0)
                {
                    Log("[TOPIC-SUGGEST] ⚠️ Không tìm thấy chủ đề gợi ý nào.");
                    SetGeminiStatus("⚠️", "Không thể gợi ý chủ đề từ kênh này. Hãy thử nhập chủ đề trực tiếp.", "#F59E0B");
                    MessageBox.Show(
                        "Không thể phân tích kênh YouTube hoặc không tìm thấy chủ đề gợi ý nào.\n\n" +
                        "Vui lòng kiểm tra:\n" +
                        "• Link kênh YouTube có đúng định dạng không? (VD: https://youtube.com/@ChannelName)\n" +
                        "• Python Server Gemini đã sẵn sàng chưa? (Kiểm tra tab Python Server Logs)\n" +
                        "• Kênh có tồn tại và có nội dung công khai không?",
                        "Không có gợi ý", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Store suggestions in task model
                taskItem.SuggestedTopics = suggestions.SuggestedTopics;
                taskItem.ChannelUrl = channelUrl;
                Log($"[TOPIC-SUGGEST] ✅ Nhận được {suggestions.SuggestedTopics.Count} chủ đề từ kênh '{suggestions.ChannelName}' ({suggestions.ChannelNiche}).");

                // Show selection dialog
                var selectedTopic = await ShowTopicSelectionDialogAsync(suggestions);

                if (selectedTopic != null)
                {
                    taskItem.Topic = selectedTopic.Title;
                    Log($"[TOPIC-SUGGEST] ✅ Người dùng đã chọn chủ đề: '{selectedTopic.Title}'");
                    SetGeminiStatus("✅", $"Đã chọn chủ đề: '{selectedTopic.Title}' — Sẵn sàng chạy pipeline!", "#10B981");
                }
                else
                {
                    SetGeminiStatus("💡", "Đã hủy chọn chủ đề. Bạn có thể nhập chủ đề thủ công hoặc thử lại.", "#6B7280");
                }
            }
            catch (Exception ex)
            {
                Log($"[TOPIC-SUGGEST-ERROR] Lỗi: {ex.Message}");
                SetGeminiStatus("❌", $"Lỗi gợi ý chủ đề: {ex.Message}", "#EF4444");
                MessageBox.Show($"Lỗi khi gợi ý chủ đề: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ResetButtonNormal(BtnSuggestTopics, "🔍 Gợi Ý Chủ Đề");
                _isGeminiOperationBusy = false;
            }
        }

        /// <summary>
        /// Shows a topic selection dialog with the suggested topics from Gemini.
        /// Returns the selected topic or null if canceled.
        /// </summary>
        private Task<SuggestedTopic?> ShowTopicSelectionDialogAsync(YoutubeTopicSuggestionResponse suggestions)
        {
            var tcs = new TaskCompletionSource<SuggestedTopic?>();

            Dispatcher.Invoke(() =>
            {
                var dialog = new Window
                {
                    Title = $"🎬 Chọn Chủ Đề — {suggestions.ChannelName} ({suggestions.ChannelNiche})",
                    Width = 620,
                    Height = 520,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.CanResizeWithGrip,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Topic list with checkboxes
                var listBox = new ListBox
                {
                    Margin = new Thickness(16, 16, 16, 8),
                    ItemsSource = suggestions.SuggestedTopics,
                    SelectionMode = SelectionMode.Single
                };

                listBox.ItemTemplate = CreateTopicItemTemplate();
                Grid.SetRow(listBox, 0);
                grid.Children.Add(listBox);

                // Bottom button bar
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(16, 0, 16, 16)
                };

                var cancelBtn = new Button
                {
                    Content = "Hủy",
                    Width = 80,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                cancelBtn.Click += (s, e) =>
                {
                    dialog.Close();
                    tcs.TrySetResult(null);
                };

                var selectBtn = new Button
                {
                    Content = "✅ Chọn Chủ Đề Này",
                    Width = 140,
                    Height = 32,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")),
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.SemiBold
                };
                selectBtn.Click += (s, e) =>
                {
                    var selected = listBox.SelectedItem as SuggestedTopic;
                    dialog.Close();
                    tcs.TrySetResult(selected);
                };

                buttonPanel.Children.Add(cancelBtn);
                buttonPanel.Children.Add(selectBtn);
                Grid.SetRow(buttonPanel, 1);
                grid.Children.Add(buttonPanel);

                dialog.Content = grid;
                dialog.Closed += (s, e) => tcs.TrySetResult(null);
                dialog.ShowDialog();
            });

            return tcs.Task;
        }

        /// <summary>
        /// Creates a DataTemplate for rendering topic items in the selection dialog.
        /// </summary>
        private static DataTemplate CreateTopicItemTemplate()
        {
            var template = new DataTemplate();

            var factory = new FrameworkElementFactory(typeof(Border));
            factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            factory.SetValue(Border.MarginProperty, new Thickness(0, 3, 0, 3));
            factory.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));
            factory.SetValue(Border.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")));
            factory.SetValue(Border.BorderBrushProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")));
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var stack = new FrameworkElementFactory(typeof(StackPanel));

            var titleBlock = new FrameworkElementFactory(typeof(TextBlock));
            titleBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
            titleBlock.SetValue(TextBlock.FontSizeProperty, 14.0);
            titleBlock.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            titleBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")));
            titleBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            stack.AppendChild(titleBlock);

            var descBlock = new FrameworkElementFactory(typeof(TextBlock));
            descBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Description"));
            descBlock.SetValue(TextBlock.FontSizeProperty, 12.0);
            descBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")));
            descBlock.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            descBlock.SetValue(TextBlock.MarginProperty, new Thickness(0, 2, 0, 4));
            stack.AppendChild(descBlock);

            var metaPanel = new FrameworkElementFactory(typeof(StackPanel));
            metaPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var catBlock = new FrameworkElementFactory(typeof(TextBlock));
            catBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Category"));
            catBlock.SetValue(TextBlock.FontSizeProperty, 11.0);
            catBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")));
            catBlock.SetValue(TextBlock.BackgroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EFF6FF")));
            catBlock.SetValue(TextBlock.PaddingProperty, new Thickness(6, 2, 6, 2));
            metaPanel.AppendChild(catBlock);

            var durBlock = new FrameworkElementFactory(typeof(TextBlock));
            durBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("EstimatedDuration"));
            durBlock.SetValue(TextBlock.FontSizeProperty, 11.0);
            durBlock.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")));
            durBlock.SetValue(TextBlock.MarginProperty, new Thickness(10, 0, 0, 0));
            metaPanel.AppendChild(durBlock);

            stack.AppendChild(metaPanel);
            factory.AppendChild(stack);

            template.VisualTree = factory;
            return template;
        }

        #region Helper Methods: Button State & Status Bar

        /// <summary>
        /// Sets a button to its loading state (disabled, busy text, wait cursor).
        /// </summary>
        private void SetButtonLoading(System.Windows.Controls.Button? btn, string loadingText)
        {
            if (btn == null) return;
            btn.IsEnabled = false;
            btn.Content = loadingText;
            btn.Cursor = System.Windows.Input.Cursors.Wait;
        }

        /// <summary>
        /// Resets a button back to its normal state (enabled, original text, arrow cursor).
        /// </summary>
        private void ResetButtonNormal(System.Windows.Controls.Button? btn, string normalText)
        {
            if (btn == null) return;
            btn.IsEnabled = true;
            btn.Content = normalText;
            btn.Cursor = System.Windows.Input.Cursors.Arrow;
        }

        /// <summary>
        /// Updates the Gemini AI Creator status bar with an icon, message, and color.
        /// </summary>
        private void SetGeminiStatus(string icon, string message, string colorHex)
        {
            Dispatcher.Invoke(() =>
            {
                if (TxtGeminiStatusIcon != null) TxtGeminiStatusIcon.Text = icon;
                if (TxtGeminiStatus != null)
                {
                    TxtGeminiStatus.Text = message;
                    TxtGeminiStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
                }
            });
        }

        /// <summary>
        /// Shows a temporary notification in the status bar that auto-clears after 5 seconds.
        /// </summary>
        private void ShowGeminiNotification(string message, string colorHex)
        {
            // Stop any previous timer
            _geminiStatusTimer?.Stop();

            Dispatcher.Invoke(() =>
            {
                if (TxtGeminiStatusIcon != null) TxtGeminiStatusIcon.Text = "🔔";
                if (TxtGeminiStatus != null)
                {
                    TxtGeminiStatus.Text = message;
                    TxtGeminiStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
                }
            });

            // Auto-clear notification after 5 seconds, reverting to default status
            _geminiStatusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _geminiStatusTimer.Tick += (s, args) =>
            {
                _geminiStatusTimer.Stop();
                SetGeminiStatus("💡", "Sẵn sàng — Nhấn 'Tải Danh Sách Gems' để làm mới hoặc 'Nạp Cookies' để đăng nhập Gemini.", "#6B7280");
            };
            _geminiStatusTimer.Start();
        }

        #endregion

        private void BtnAddGeminiTask_Click(object sender, RoutedEventArgs e)
        {
            InitializeGeminiCreatorServices();
            var newTask = _geminiCreatorService!.CreateDefaultTask(
                AvailableScriptwriterGems,
                AvailableSceneCreatorGems,
                $"Chủ đề video mới #{GeminiTasks.Count + 1}"
            );
            GeminiTasks.Add(newTask);
            Log($"[INFO] Đã thêm task mới vào hàng đợi (Tổng: {GeminiTasks.Count} tasks).");
        }

        /// <summary>
        /// Clears the Python Server log panel.
        /// </summary>
        private void BtnClearPythonServerLog_Click(object sender, RoutedEventArgs e)
        {
            if (TxtPythonServerLog != null)
            {
                TxtPythonServerLog.Clear();
                Log("[INFO] Đã xóa Python Server log panel.");
            }
        }

        /// <summary>
        /// Toggles the collapse/expand state of the Python Server Logs panel.
        /// </summary>
        private void BtnTogglePythonLogs_Click(object sender, RoutedEventArgs e)
        {
            if (TxtPythonServerLog == null) return;

            bool isCollapsed = TxtPythonServerLog.Visibility == Visibility.Collapsed;
            if (isCollapsed)
            {
                // Expand
                TxtPythonServerLog.Visibility = Visibility.Visible;
                if (sender is Button btn)
                    btn.Content = "▼";
            }
            else
            {
                // Collapse
                TxtPythonServerLog.Visibility = Visibility.Collapsed;
                if (sender is Button btn)
                    btn.Content = "▶";
            }
        }

        /// <summary>
        /// Collapses the currently expanded Gemini task row details.
        /// </summary>
        private void BtnCollapseGeminiRowDetails_Click(object sender, RoutedEventArgs e)
        {
            DgridGeminiTasks.SelectedItem = null;
        }

        private void BtnDeleteGeminiTasks_Click(object sender, RoutedEventArgs e)
        {
            var selectedTasks = GeminiTasks.Where(t => t.IsSelected).ToList();
            if (selectedTasks.Count == 0)
            {
                MessageBox.Show("Vui lòng chọn ít nhất 1 task để xóa!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show($"Bạn có chắc chắn muốn xóa {selectedTasks.Count} task đã chọn?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var t in selectedTasks)
                {
                    GeminiTasks.Remove(t);
                }
                Log($"[INFO] Đã xóa {selectedTasks.Count} task khỏi bảng.");
            }
        }

        private void ChkSelectAllGeminiTasks_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk)
            {
                bool isChecked = chk.IsChecked ?? false;
                foreach (var t in GeminiTasks)
                {
                    t.IsSelected = isChecked;
                }
            }
        }

        private async void BtnRunSelectedGeminiTasks_Click(object sender, RoutedEventArgs e)
        {
            var selectedTasks = GeminiTasks.Where(t => t.IsSelected).ToList();
            if (selectedTasks.Count == 0)
            {
                MessageBox.Show("Vui lòng tích chọn ít nhất 1 task trong bảng để thực thi!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            InitializeGeminiCreatorServices();
            if (selectedTasks.Count > 0)
            {
                OpenTaskLogSidebar(selectedTasks[0]);
            }

            // Đánh dấu đang bận để chặn các thao tác khác
            _isGeminiOperationBusy = true;

            try
            {
                Log($"[GEMINI-BATCH] 🚀 Bắt đầu thực thi chuỗi {selectedTasks.Count} tasks qua Pipeline Orchestrator...");
                Log($"[GEMINI-BATCH] 📊 Slot: DeepRsrch=2 | Voiceover=3 | SceneCreator=4 | ImageGen=1");

                // Resolve language cho từng task nếu cần
                string apiKey = ConfigService.CurrentSettings.Ai84ApiKey;
                foreach (var t in selectedTasks)
                {
                    if (string.IsNullOrEmpty(t.TargetLanguage) && !string.IsNullOrEmpty(t.VoiceId))
                    {
                        await _geminiCreatorService!.ResolveTaskLanguageAsync(t, apiKey);
                    }
                }

                // ── Gọi Pipeline Orchestrator (chạy trên thread pool) ──
                var result = await Task.Run(() =>
                    _pipelineOrchestrator!.ExecuteBatchAsync(
                        taskModels: selectedTasks,
                        logTask: (taskModel, msg) =>
                        {
                            // Dispatch về UI thread để cập nhật giao diện
                            Dispatcher.Invoke(() =>
                            {
                                AppendGeminiTaskLog(taskModel, msg);
                                UpdateTaskStepInfoFromLog(taskModel, msg);
                            });
                        }
                    )
                );

                Log($"[GEMINI-BATCH] 🎉 {result}");
                MessageBox.Show(
                    $"Đã hoàn thành thực thi {result.TotalTasks} Gemini tasks!\n\n" +
                    $"✅ Thành công: {result.SuccessCount}\n" +
                    $"❌ Thất bại: {result.FailedCount}\n" +
                    $"⏱️ Thời gian: {result.Elapsed.TotalMinutes:F1} phút",
                    "Pipeline Orchestrator — Kết Quả",
                    MessageBoxButton.OK,
                    result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                Log($"[GEMINI-BATCH-ERROR] Lỗi pipeline orchestrator: {ex.Message}");
                MessageBox.Show($"Lỗi khi chạy batch pipeline: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isGeminiOperationBusy = false;
            }
        }

        private async void BtnRunSingleGeminiTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                InitializeGeminiCreatorServices();
                OpenTaskLogSidebar(taskItem);
                await ExecuteSingleGeminiTaskAsync(taskItem);
            }
        }

        private async Task ExecuteSingleGeminiTaskAsync(GeminiTaskModel taskItem)
        {
            string topic = taskItem.Topic.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                taskItem.Status = NodeStatus.Failed;
                taskItem.CurrentStepInfo = "Lỗi: Chưa nhập topic";
                AppendGeminiTaskLog(taskItem, $"[ERROR] Task ID {taskItem.Id[..6]} bị bỏ qua vì chưa có topic.");
                return;
            }

            string scriptwriterGemId = taskItem.SelectedScriptwriterGem?.Id ?? string.Empty;
            string sceneCreatorGemId = taskItem.SelectedSceneCreatorGem?.Id ?? string.Empty;
            string sceneCreatorGemName = taskItem.SelectedSceneCreatorGem?.Name ?? string.Empty;
            string voiceId = taskItem.VoiceId.Trim();
            bool enableDeepResearch = taskItem.EnableDeepResearch;
            string providerKey = taskItem.SelectedImageProvider ?? "flow_local";
            string scriptwriterModel = taskItem.ScriptwriterModel ?? "gemini-3-flash";
            string sceneCreatorModel = taskItem.SceneCreatorModel ?? "gemini-3-flash";

            string resolvedScript = GeminiApiService.ResolveModelName(scriptwriterModel);
            string resolvedScene = GeminiApiService.ResolveModelName(sceneCreatorModel);

            taskItem.Status = NodeStatus.Running;
            taskItem.CurrentStepInfo = "Khởi chạy Pipeline...";

            if (string.IsNullOrEmpty(taskItem.TargetLanguage) && !string.IsNullOrEmpty(voiceId))
            {
                await _geminiCreatorService!.ResolveTaskLanguageAsync(taskItem, ConfigService.CurrentSettings.Ai84ApiKey);
            }

            string targetLang = !string.IsNullOrWhiteSpace(taskItem.TargetLanguage)
                ? taskItem.TargetLanguage
                : "English - en";

            var task = new AutomationTask
            {
                VideoUrl = topic,
                VoiceId = voiceId,
                TargetLanguage = targetLang,
                Step1 = false,
                Step2 = true,  // Deep Research Transcript
                Step3 = true,  // Scene Breakdown
                Step4 = true,  // Voiceover
                Step5 = true,  // Batch Image Gen
                StepSrt = true
            };

            // Generate clean lowercase slug folder name (e.g. "nighttime-loneliness-fomo")
            if (string.IsNullOrEmpty(taskItem.OutputFolderName))
            {
                taskItem.OutputFolderName = YoutubeHelper.ToSafeTopicSlug(topic);
            }
            task.OutputFolderOverride = taskItem.OutputFolderName;

            AppendGeminiTaskLog(taskItem, $"[TASK-RUN] 🚀 Đang xử lý task '{topic}' (Script: {resolvedScript}, Scene: {resolvedScene})...");
            AppendGeminiTaskLog(taskItem, $"[TASK-RUN] 📁 Thư mục output: {task.OutputDir}");

            try
            {
                await Task.Run(async () =>
                {
                    await _geminiVideoPipelineService!.ExecutePipelineAsync(
                        task: task,
                        topicOrUrl: topic,
                        scriptwriterGemId: scriptwriterGemId,
                        sceneCreatorGemId: sceneCreatorGemId,
                        sceneCreatorGemName: sceneCreatorGemName,
                        enableDeepResearch: enableDeepResearch,
                        voiceId: voiceId,
                        imageGenProvider: providerKey,
                        logTask: (t, msg) => Dispatcher.Invoke(() =>
                        {
                            AppendGeminiTaskLog(taskItem, msg);
                            UpdateTaskStepInfoFromLog(taskItem, msg);
                        }),
                        scriptwriterModel: resolvedScript,
                        sceneCreatorModel: resolvedScene
                    );
                });

                taskItem.Status = NodeStatus.Success;
                taskItem.CurrentStepInfo = "✔️ Hoàn thành 100%";
                AppendGeminiTaskLog(taskItem, $"[TASK-SUCCESS] 🎉 Task '{topic}' đã hoàn thành xuất sắc!");
            }
            catch (Exception ex)
            {
                taskItem.Status = NodeStatus.Failed;
                taskItem.CurrentStepInfo = $"❌ Lỗi: {ex.Message}";
                AppendGeminiTaskLog(taskItem, $"[TASK-ERROR] Lỗi thực thi task '{topic}': {ex.Message}");
            }
        }

        private void UpdateTaskStepInfoFromLog(GeminiTaskModel taskItem, string msg)
        {
            bool isSuccess = msg.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
                             msg.Contains("⏭️", StringComparison.OrdinalIgnoreCase) ||
                             msg.Contains("Completed", StringComparison.OrdinalIgnoreCase) ||
                             msg.Contains("Hoàn thành", StringComparison.OrdinalIgnoreCase);

            if (msg.Contains("DEEP-RESEARCH-POLL", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step1Status = NodeStatus.Running;
                int elapsedIdx = msg.IndexOf("Elapsed:", StringComparison.OrdinalIgnoreCase);
                if (elapsedIdx >= 0)
                {
                    taskItem.CurrentStepInfo = $"Step 1: Deep Research ({msg.Substring(elapsedIdx).Trim()})";
                }
                else
                {
                    taskItem.CurrentStepInfo = "Step 1: Deep Research (Đang nghiên cứu...)";
                }
                taskItem.Step1Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }
            else if (msg.Contains("STEP 1 & 2", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("[STEP 2]", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 1", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Deep Research", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("research_report", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("transcript.txt", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Transcript", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("script", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step1Status = isSuccess ? NodeStatus.Success : NodeStatus.Running;
                taskItem.CurrentStepInfo = isSuccess ? "Step 1: ✔️ Done" : "Step 1: Deep Research & Transcript";
                taskItem.Step1Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }
            else if (msg.Contains("STEP 3", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("VOICEOVER", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Voiceover", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("SRT", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("AI84", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("transcriptUrl", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("text-to-speech", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("TTS Job", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("voiceover.mp3", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("voiceover.srt", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("voiceover.wav", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step2Status = isSuccess ? NodeStatus.Success : NodeStatus.Running;
                taskItem.CurrentStepInfo = isSuccess ? "Step 2: ✔️ Done" : "Step 2: Voiceover & SRT";
                taskItem.Step2Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }
            else if (msg.Contains("[STEP 4]", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Scene Creator", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Scene Breakdown", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("scenes.json", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("scene_count", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step3Status = isSuccess ? NodeStatus.Success : NodeStatus.Running;
                taskItem.CurrentStepInfo = isSuccess ? "Step 3: ✔️ Done" : "Step 3: Scene Breakdown & Prompts";
                taskItem.Step3Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }
            else if (msg.Contains("[STEP 5]", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Batch Image", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Image Generation", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("scene_", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step4Status = isSuccess ? NodeStatus.Success : NodeStatus.Running;
                taskItem.CurrentStepInfo = isSuccess ? "Step 4: ✔️ Done" : "Step 4: Image Generation";
                taskItem.Step4Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }
            else
            {
                // General pipeline logs go to current active step
                if (taskItem.Step4Status == NodeStatus.Running) taskItem.Step4Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                else if (taskItem.Step3Status == NodeStatus.Running) taskItem.Step3Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                else if (taskItem.Step2Status == NodeStatus.Running) taskItem.Step2Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                else taskItem.Step1Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            }

            if (SidebarLogs.Visibility == Visibility.Visible && _activeGeminiTaskLog == taskItem)
            {
                UpdateGeminiStepAccordionView(taskItem);
            }
        }

        private void BtnBrowseCharacterRef_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image Files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All Files|*.*",
                    Title = "Chọn ảnh nhân vật gốc (Character Reference)"
                };

                if (dlg.ShowDialog() == true)
                {
                    taskItem.CharacterRef = dlg.FileName;
                }
            }
        }

        private void BtnViewTaskScenesJson_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                // Use stored OutputFolderName (timestamp-based) if available, else fall back
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.Instance.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.Instance.GetOutputDir(folderKey);
                string scenesPath = Path.Combine(outputDir, "scenes.json");

                if (!File.Exists(scenesPath))
                {
                    MessageBox.Show($"Không tìm thấy file scenes.json tại:\n{scenesPath}.\nVui lòng chạy task trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    string jsonText = File.ReadAllText(scenesPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);

                    if (rootData != null)
                    {
                        var viewerWin = new AssetAutomator.UI.Windows.ScenesViewerWindow(rootData);
                        viewerWin.Owner = this;
                        viewerWin.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể đọc scenes.json: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOpenTaskFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                // Use stored OutputFolderName (timestamp-based) if available, else fall back
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.Instance.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.Instance.GetOutputDir(folderKey);

                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = outputDir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnDeleteSingleGeminiTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa task '{taskItem.Topic}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    GeminiTasks.Remove(taskItem);
                }
            }
        }

        /// <summary>
        /// Shows a context menu with secondary actions (Logs, Assets, Delete) when clicking ⋮.
        /// </summary>
        private void BtnGeminiTaskMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not GeminiTaskModel taskItem) return;

            var contextMenu = new ContextMenu();

            var logsItem = new MenuItem { Header = "📋 Xem Logs" };
            logsItem.Click += (s, args) => OpenTaskLogSidebar(taskItem);
            contextMenu.Items.Add(logsItem);

            var assetsItem = new MenuItem { Header = "🎬 Xem Assets (scenes.json)" };
            assetsItem.Click += (s, args) =>
            {
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.Instance.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.Instance.GetOutputDir(folderKey);
                string scenesPath = Path.Combine(outputDir, "scenes.json");
                if (!File.Exists(scenesPath))
                {
                    MessageBox.Show($"Không tìm thấy file scenes.json tại:\n{scenesPath}.\nVui lòng chạy task trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    string jsonText = File.ReadAllText(scenesPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);
                    if (rootData != null)
                    {
                        var viewerWin = new AssetAutomator.UI.Windows.ScenesViewerWindow(rootData) { Owner = this };
                        viewerWin.ShowDialog();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể đọc scenes.json: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            contextMenu.Items.Add(assetsItem);

            var folderItem = new MenuItem { Header = "📁 Mở Thư Mục Output" };
            folderItem.Click += (s, args) =>
            {
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.Instance.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.Instance.GetOutputDir(folderKey);
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = outputDir, UseShellExecute = true, Verb = "open" });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Không thể mở thư mục: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            contextMenu.Items.Add(folderItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "🗑️ Xóa Task", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")) };
            deleteItem.Click += (s, args) =>
            {
                var result = MessageBox.Show($"Bạn có chắc chắn muốn xóa task '{taskItem.Topic}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) GeminiTasks.Remove(taskItem);
            };
            contextMenu.Items.Add(deleteItem);

            contextMenu.IsOpen = true;
        }

        private void BtnViewSingleGeminiTaskLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GeminiTaskModel taskItem)
            {
                OpenTaskLogSidebar(taskItem);
            }
        }

        private GeminiTaskModel? _activeGeminiTaskLog;

        private void OpenTaskLogSidebar(GeminiTaskModel taskItem)
        {
            _activeGeminiTaskLog = taskItem;
            TxtSidebarLog.Visibility = Visibility.Collapsed;
            GridGeminiStepAccordion.Visibility = Visibility.Visible;

            UpdateGeminiStepAccordionView(taskItem);

            SidebarLogs.Width = _sidebarWidth;
            SidebarLogs.Visibility = Visibility.Visible;
        }

        private void UpdateGeminiStepAccordionView(GeminiTaskModel taskItem)
        {
            UpdateStepBadge(BadgeStep1, TxtBadgeStep1, taskItem.Step1Status);
            UpdateStepBadge(BadgeStep2, TxtBadgeStep2, taskItem.Step2Status);
            UpdateStepBadge(BadgeStep3, TxtBadgeStep3, taskItem.Step3Status);
            UpdateStepBadge(BadgeStep4, TxtBadgeStep4, taskItem.Step4Status);
            UpdateStepBadge(BadgeStep5, TxtBadgeStep5, taskItem.Step5Status);

            SetLogTextToRichTextBox(TxtLogStep1, string.IsNullOrWhiteSpace(taskItem.Step1Logs) ? "Chưa có log cho bước này." : taskItem.Step1Logs);
            SetLogTextToRichTextBox(TxtLogStep2, string.IsNullOrWhiteSpace(taskItem.Step2Logs) ? "Chưa có log cho bước này." : taskItem.Step2Logs);
            SetLogTextToRichTextBox(TxtLogStep3, string.IsNullOrWhiteSpace(taskItem.Step3Logs) ? "Chưa có log cho bước này." : taskItem.Step3Logs);
            SetLogTextToRichTextBox(TxtLogStep4, string.IsNullOrWhiteSpace(taskItem.Step4Logs) ? "Chưa có log cho bước này." : taskItem.Step4Logs);
            SetLogTextToRichTextBox(TxtLogStep5, string.IsNullOrWhiteSpace(taskItem.Step5Logs) ? "Chưa có log cho bước này." : taskItem.Step5Logs);

            // Auto-expand running step
            if (taskItem.Step5Status == NodeStatus.Running) { ExpanderStep5.IsExpanded = true; }
            else if (taskItem.Step4Status == NodeStatus.Running) { ExpanderStep4.IsExpanded = true; }
            else if (taskItem.Step3Status == NodeStatus.Running) { ExpanderStep3.IsExpanded = true; }
            else if (taskItem.Step2Status == NodeStatus.Running) { ExpanderStep2.IsExpanded = true; }
            else { ExpanderStep1.IsExpanded = true; }
        }

        private void UpdateStepBadge(Border badgeBorder, TextBlock badgeText, NodeStatus status)
        {
            switch (status)
            {
                case NodeStatus.Running:
                    badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    badgeText.Text = "⏳ Đang chạy...";
                    break;
                case NodeStatus.Success:
                    badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    badgeText.Text = "✔️ Hoàn thành";
                    break;
                case NodeStatus.Failed:
                    badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                    badgeText.Text = "❌ Lỗi";
                    break;
                default:
                    badgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                    badgeText.Text = "⚪ Chờ";
                    break;
            }
        }

        private void SetLogTextToRichTextBox(RichTextBox rtb, string text)
        {
            rtb.Document.Blocks.Clear();
            var p = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            p.Inlines.Add(new Run(text));
            rtb.Document.Blocks.Add(p);
            rtb.ScrollToEnd();
        }

        private void AppendGeminiTaskLog(GeminiTaskModel taskItem, string message)
        {
            string formattedLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
            if (string.IsNullOrEmpty(taskItem.Logs))
            {
                taskItem.Logs = formattedLine;
            }
            else
            {
                taskItem.Logs += "\n" + formattedLine;
            }
        }
    }
}
