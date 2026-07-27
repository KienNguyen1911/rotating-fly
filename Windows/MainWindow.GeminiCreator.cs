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
using AssetAutomator.Helpers;
using AssetAutomator.Models;
using AssetAutomator.Models.Nodes;
using AssetAutomator.Services;

namespace AssetAutomator
{
    public partial class MainWindow : Window
    {
        private GeminiApiService? _geminiApiService;
        private GeminiVideoPipelineService? _geminiVideoPipelineService;
        private bool _isGeminiOperationBusy;
        private System.Windows.Threading.DispatcherTimer? _geminiStatusTimer;

        public ObservableCollection<GeminiTaskModel> GeminiTasks { get; } = new();
        public ObservableCollection<GemOptionItem> AvailableScriptwriterGems { get; } = new();
        public ObservableCollection<GemOptionItem> AvailableSceneCreatorGems { get; } = new();
        public ObservableCollection<string> AvailableImageProviders { get; } = new() { "flow_local", "glabs" };
        public ObservableCollection<string> AvailableAiModels { get; } = new() { "3.6 Flash", "3.5 Flash-Lite", "3.1 Pro" };
        public ObservableCollection<string> AvailableExtensions { get; } = new() { "Tắt (Standard)", "Bật (Tư duy mở rộng)" };

        private void InitializeGeminiCreatorServices()
        {
            _geminiApiService ??= new GeminiApiService();

            // Lazy initialize step services for Gemini Pipeline
            if (_geminiVideoPipelineService == null)
            {
                var batchImageGenService = new BatchImageGenService();
                var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);
                var voiceoverStep = new VoiceoverGenerationStep();
                var sceneBreakdownStep = new GeminiSceneBreakdownStep(_geminiApiService);
                var imageBatchStep = new SceneImageBatchStep(batchImageGenService);

                _geminiVideoPipelineService = new GeminiVideoPipelineService(
                    topicResearchStep,
                    voiceoverStep,
                    sceneBreakdownStep,
                    imageBatchStep
                );
            }

            // Add default initial task if empty
            if (GeminiTasks.Count == 0)
            {
                GeminiTasks.Add(CreateDefaultGeminiTask());
            }

            // Load gems ONCE on initial setup if not yet loaded
            if (AvailableScriptwriterGems.Count == 0)
            {
                _ = LoadGeminiGemsToComboboxesAsync();
            }
        }

        private GeminiTaskModel CreateDefaultGeminiTask(string? topic = null)
        {
            var defaultScriptwriter = AvailableScriptwriterGems.FirstOrDefault();
            var defaultSceneCreator = AvailableSceneCreatorGems.FirstOrDefault();

            return new GeminiTaskModel
            {
                Topic = topic ?? "",
                SelectedScriptwriterGem = defaultScriptwriter,
                SelectedSceneCreatorGem = defaultSceneCreator,
                EnableDeepResearch = true,
                VoiceId = "",
                SelectedImageProvider = "flow_local",
                Status = NodeStatus.Idle,
                CurrentStepInfo = "Sẵn sàng"
            };
        }

        private async Task LoadGeminiGemsToComboboxesAsync()
        {
            try
            {
                _geminiApiService ??= new GeminiApiService();
                var gems = await _geminiApiService.GetGemsAsync(includeHidden: true);

                Dispatcher.Invoke(() =>
                {
                    // Snapshot existing selected Gem IDs for all tasks
                    var existingSelections = GeminiTasks.Select(t => new
                    {
                        Task = t,
                        ScriptwriterId = t.SelectedScriptwriterGem?.Id ?? string.Empty,
                        SceneCreatorId = t.SelectedSceneCreatorGem?.Id ?? string.Empty
                    }).ToList();

                    // Ensure default options exist without clearing the collections
                    var defaultScriptwriterGem = AvailableScriptwriterGems.FirstOrDefault(g => g.Id == string.Empty);
                    if (defaultScriptwriterGem == null)
                    {
                        defaultScriptwriterGem = new GemOptionItem { Id = string.Empty, Name = "-- Gemini Mặc Định --" };
                        AvailableScriptwriterGems.Insert(0, defaultScriptwriterGem);
                    }

                    var defaultSceneCreatorGem = AvailableSceneCreatorGems.FirstOrDefault(g => g.Id == string.Empty);
                    if (defaultSceneCreatorGem == null)
                    {
                        defaultSceneCreatorGem = new GemOptionItem { Id = string.Empty, Name = "-- Gemini Mặc Định --" };
                        AvailableSceneCreatorGems.Insert(0, defaultSceneCreatorGem);
                    }

                    GemOptionItem? configScriptwriter = defaultScriptwriterGem;
                    GemOptionItem? configSceneCreator = defaultSceneCreatorGem;

                    // Filter only Custom Gems (predefined == false)
                    var customGems = gems.Where(g => !g.predefined).ToList();

                    foreach (var gem in customGems)
                    {
                        var existingScriptwriter = AvailableScriptwriterGems.FirstOrDefault(g => g.Id.Equals(gem.id, StringComparison.OrdinalIgnoreCase));
                        if (existingScriptwriter == null)
                        {
                            existingScriptwriter = new GemOptionItem { Id = gem.id, Name = gem.name };
                            AvailableScriptwriterGems.Add(existingScriptwriter);
                        }
                        else
                        {
                            existingScriptwriter.Name = gem.name;
                        }

                        var existingSceneCreator = AvailableSceneCreatorGems.FirstOrDefault(g => g.Id.Equals(gem.id, StringComparison.OrdinalIgnoreCase));
                        if (existingSceneCreator == null)
                        {
                            existingSceneCreator = new GemOptionItem { Id = gem.id, Name = gem.name };
                            AvailableSceneCreatorGems.Add(existingSceneCreator);
                        }
                        else
                        {
                            existingSceneCreator.Name = gem.name;
                        }

                        if (gem.id.Equals(ConfigService.CurrentSettings.ScriptwriterGemId, StringComparison.OrdinalIgnoreCase) ||
                            (configScriptwriter == defaultScriptwriterGem && (gem.name.Contains("psychology", StringComparison.OrdinalIgnoreCase) || gem.name.Contains("bedtime", StringComparison.OrdinalIgnoreCase))))
                        {
                            configScriptwriter = existingScriptwriter;
                        }
                        if (gem.id.Equals(ConfigService.CurrentSettings.SceneCreatorGemId, StringComparison.OrdinalIgnoreCase) ||
                            (configSceneCreator == defaultSceneCreatorGem && (gem.name.Contains("scriptor", StringComparison.OrdinalIgnoreCase) || gem.name.Contains("rewrite", StringComparison.OrdinalIgnoreCase) || gem.name.Contains("scene", StringComparison.OrdinalIgnoreCase))))
                        {
                            configSceneCreator = existingSceneCreator;
                        }
                    }

                    // Restore / preserve selection for existing tasks based on preserved IDs
                    foreach (var sel in existingSelections)
                    {
                        var matchScriptwriter = AvailableScriptwriterGems.FirstOrDefault(g => g.Id.Equals(sel.ScriptwriterId, StringComparison.OrdinalIgnoreCase));
                        var matchSceneCreator = AvailableSceneCreatorGems.FirstOrDefault(g => g.Id.Equals(sel.SceneCreatorId, StringComparison.OrdinalIgnoreCase));

                        if (matchScriptwriter != null)
                        {
                            sel.Task.SelectedScriptwriterGem = matchScriptwriter;
                        }
                        else if (sel.Task.SelectedScriptwriterGem == null)
                        {
                            sel.Task.SelectedScriptwriterGem = configScriptwriter;
                        }

                        if (matchSceneCreator != null)
                        {
                            sel.Task.SelectedSceneCreatorGem = matchSceneCreator;
                        }
                        else if (sel.Task.SelectedSceneCreatorGem == null)
                        {
                            sel.Task.SelectedSceneCreatorGem = configSceneCreator;
                        }
                    }

                    Log($"[SUCCESS] Đã làm mới danh sách Gems! Đã nạp {customGems.Count} Custom Gems.");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    Log($"[ERROR] Không thể tải danh sách Gemini Gems: {ex.Message}");
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

            var syncService = new GeminiCookieSyncService(msg => Log(msg));
            _geminiApiService ??= new GeminiApiService();

            bool cookieImportedSuccess = false;

            try
            {
                if (choice == MessageBoxResult.Yes)
                {
                    Log("[COOKIE-IMPORT] ⏳ Đang tự động quét & trích xuất Cookies Gemini từ các Chrome Profiles...");
                    SetGeminiStatus("🔍", "Đang quét Chrome Profiles để tìm Cookies Gemini...", "#3B82F6");

                    bool ok = await syncService.AutoSyncFromSystemChromeAsync();

                    if (ok)
                    {
                        cookieImportedSuccess = true;
                    }
                    else
                    {
                        // Fallback: Open interactive browser for user to login if no active cookies found
                        var confirmLogin = MessageBox.Show(
                            "Chưa tìm thấy phiên đăng nhập Gemini sẵn có trong các Profile Chrome.\n\n" +
                            "Bạn có muốn mở Chrome để đăng nhập https://gemini.google.com ngay bây giờ không?",
                            "Yêu cầu Đăng nhập Gemini",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information
                        );

                        if (confirmLogin == MessageBoxResult.Yes)
                        {
                            Log("[COOKIE-IMPORT] 🌐 Đang mở Chrome để bạn hoàn tất đăng nhập Gemini...");
                            SetGeminiStatus("🌐", "Đang mở Chrome — vui lòng đăng nhập Gemini trên trình duyệt...", "#3B82F6");

                            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
                            string tempProfilePath = Path.Combine(Path.GetTempPath(), "GeminiManualLogin_" + Guid.NewGuid().ToString("N"));
                            try
                            {
                                var context = await playwright.Chromium.LaunchPersistentContextAsync(
                                    tempProfilePath,
                                    new Microsoft.Playwright.BrowserTypeLaunchPersistentContextOptions
                                    {
                                        Headless = false,
                                        Channel = "chrome",
                                        Args = new[] { "--disable-blink-features=AutomationControlled", "--no-sandbox" }
                                    });

                                var page = await context.NewPageAsync();
                                await page.GotoAsync("https://gemini.google.com");

                                MessageBox.Show(
                                    "Vui lòng hoàn tất đăng nhập Google trên cửa sổ Chrome vừa mở.\n\n" +
                                    "Sau khi đăng nhập thành công và nhìn thấy trang chính Gemini, bấm OK tại bảng này để lưu Cookies.",
                                    "Xác nhận đăng nhập",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information
                                );

                                SetGeminiStatus("💾", "Đang lưu Cookies từ trình duyệt...", "#3B82F6");

                                cookieImportedSuccess = await syncService.SyncCookiesFromBrowserContextAsync(context);
                                await context.CloseAsync();
                            }
                            catch (Exception ex)
                            {
                                Log($"[COOKIE-IMPORT] ❌ Lỗi khi mở Chrome đăng nhập: {ex.Message}");
                            }
                            finally
                            {
                                if (Directory.Exists(tempProfilePath))
                                {
                                    try { Directory.Delete(tempProfilePath, true); } catch { }
                                }
                            }
                        }
                    }
                }
                else if (choice == MessageBoxResult.No)
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
                            cookieImportedSuccess = await syncService.SaveCustomCookiesJsonAsync(content);
                        }
                        catch (Exception ex)
                        {
                            Log($"[COOKIE-IMPORT] ❌ Lỗi khi đọc file cookies: {ex.Message}");
                            MessageBox.Show($"Lỗi khi đọc file cookies: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }

                if (cookieImportedSuccess)
                {
                    Log("[COOKIE-IMPORT] 🔄 Đang tự động khởi động lại module Gemini Python Server (Modules/Gemini-API-2.0.0)...");
                    SetGeminiStatus("🔄", "Đang khởi động lại Gemini Python Server...", "#3B82F6");

                    bool restarted = await Helpers.PythonServerManager.RestartServerAsync();

                    if (restarted)
                    {
                        Log("[COOKIE-IMPORT] 🎉 Module Gemini API Server đã được khởi động lại thành công với Cookie mới!");
                        SetGeminiStatus("✅", "Cookies đã được nạp & Server đã khởi động lại!", "#10B981");
                        ShowGeminiNotification("🔑 Nạp Cookies Gemini thành công! Server đã sẵn sàng.", "#10B981");

                        // Reload available gems list
                        SetGeminiStatus("🔄", "Đang làm mới danh sách Gems...", "#3B82F6");
                        await LoadGeminiGemsToComboboxesAsync();
                        SetGeminiStatus("✅", "Tất cả đã sẵn sàng! Cookies + Gems đã được cập nhật.", "#10B981");
                    }
                    else
                    {
                        Log("[COOKIE-IMPORT] ⚠️ Đã lưu cookies.json nhưng không thể tự động khởi động lại Python Server.");
                        SetGeminiStatus("⚠️", "Đã lưu cookies nhưng không khởi động lại được Server.", "#F59E0B");
                        ShowGeminiNotification("⚠️ Đã lưu cookies.json. Vui lòng kiểm tra lại Python Server.", "#F59E0B");
                    }
                }
                else
                {
                    SetGeminiStatus("ℹ️", "Chưa có Cookies nào được nạp.", "#6B7280");
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
            var newTask = CreateDefaultGeminiTask($"Chủ đề video mới #{GeminiTasks.Count + 1}");
            GeminiTasks.Add(newTask);
            Log($"[INFO] Đã thêm task mới vào hàng đợi (Tổng: {GeminiTasks.Count} tasks).");
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

            Log($"[GEMINI-BATCH] 🚀 Bắt đầu thực thi chuỗi {selectedTasks.Count} tasks...");

            foreach (var taskItem in selectedTasks)
            {
                await ExecuteSingleGeminiTaskAsync(taskItem);
            }

            MessageBox.Show($"Đã hoàn thành thực thi chuỗi {selectedTasks.Count} Gemini tasks!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
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
            string voiceId = taskItem.VoiceId.Trim();
            bool enableDeepResearch = taskItem.EnableDeepResearch;
            string providerKey = taskItem.SelectedImageProvider ?? "flow_local";
            string selectedModel = taskItem.SelectedModel ?? "gemini-3-flash";
            string selectedExtension = taskItem.SelectedExtension ?? "None";

            taskItem.Status = NodeStatus.Running;
            taskItem.CurrentStepInfo = "Khởi chạy Pipeline...";

            if (string.IsNullOrEmpty(taskItem.TargetLanguage) && !string.IsNullOrEmpty(voiceId))
            {
                await ResolveGeminiTaskLanguageAsync(taskItem, ConfigService.CurrentSettings.Ai84ApiKey);
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

            AppendGeminiTaskLog(taskItem, $"[TASK-RUN] 🚀 Đang xử lý task '{topic}' (Model: {selectedModel}, Ext: {selectedExtension})...");
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
                        enableDeepResearch: enableDeepResearch,
                        voiceId: voiceId,
                        imageGenProvider: providerKey,
                        logTask: (t, msg) => Dispatcher.Invoke(() =>
                        {
                            AppendGeminiTaskLog(taskItem, msg);
                            UpdateTaskStepInfoFromLog(taskItem, msg);
                        }),
                        model: selectedModel,
                        extension: selectedExtension
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
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.GetOutputDir(folderKey);
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
                        var viewerWin = new Windows.ScenesViewerWindow(rootData);
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
                string folderKey = taskItem.OutputFolderName ?? YoutubeHelper.ExtractVideoId(taskItem.Topic.Trim());
                string outputDir = YoutubeHelper.GetOutputDir(folderKey);

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
