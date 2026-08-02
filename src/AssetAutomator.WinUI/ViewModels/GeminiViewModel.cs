using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Steps;
using AssetAutomator.Infrastructure.Helpers;
using AssetAutomator.Infrastructure.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace AssetAutomator.WinUI.ViewModels;

public partial class GeminiViewModel : ObservableObject
{
    private readonly GeminiCreatorService? _geminiCreatorService;
    private readonly PipelineOrchestrator? _pipelineOrchestrator;
    private readonly YoutubeTopicSuggestionStep? _topicSuggestionStep;
    private readonly GeminiApiService? _geminiApiService;
    private readonly PythonServerManager? _pythonServerManager;
    private readonly IConfigService? _configService;
    private readonly ILogService? _logService;
    private readonly IBrowserService? _browserService;

    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<GeminiTaskModel> GeminiTasks { get; } = new();
    public ObservableCollection<GemOptionItem> AvailableScriptwriterGems { get; } = new();
    public ObservableCollection<GemOptionItem> AvailableSceneCreatorGems { get; } = new();
    public ObservableCollection<string> AvailableImageProviders { get; } = new() { "flow_local", "glabs" };

    public ObservableCollection<string> AvailableAiModels { get; } = new()
    {
        "gemini-3-flash",
        "gemini-3-pro",
        "gemini-3-flash-thinking",
        "gemini-3-flash-plus",
        "gemini-3-pro-plus",
        "gemini-3-flash-thinking-plus",
        "gemini-3-flash-advanced",
        "gemini-3-pro-advanced",
        "gemini-3-flash-thinking-advanced",
    };

    [ObservableProperty]
    private GeminiTaskModel? _selectedTask;

    [ObservableProperty]
    private bool _selectAllTasks;

    [ObservableProperty]
    private string _statusLog = "Sẵn sàng khởi tạo hàng đợi Gemini AI Creator.";

    [ObservableProperty]
    private string _consoleLogs = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private bool _isLoadingGems;

    [ObservableProperty]
    private bool _isImportingCookies;

    [ObservableProperty]
    private bool _isSuggestingTopics;

    [ObservableProperty]
    private int _videoDurationMinutes = 1;

    // ─────────────────────────────────────────────────────
    //  Task Live Logs Drawer & Console Log Control
    // ─────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isTaskLogsDrawerOpen;

    [ObservableProperty]
    private double _taskLogsDrawerWidth = 450;

    [ObservableProperty]
    private bool _isRowDetailsDrawerOpen;

    [RelayCommand]
    private void CloseRowDetailsDrawer()
    {
        IsRowDetailsDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenTaskLogs(GeminiTaskModel? task)
    {
        if (task != null)
        {
            SelectedTask = task;
        }
        IsTaskLogsDrawerOpen = true;
    }

    [RelayCommand]
    private void CloseTaskLogs()
    {
        IsTaskLogsDrawerOpen = false;
    }

    /// <summary>True = bottom Console Logs panel is shown. Default false to keep page compact.</summary>
    [ObservableProperty]
    private bool _isConsoleLogVisible;

    [RelayCommand]
    private void ToggleConsoleLog()
    {
        IsConsoleLogVisible = !IsConsoleLogVisible;
    }

    // ─────────────────────────────────────────────────────
    //  C7 — 5-Step Accordion helpers
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a brush for a step badge based on the step status.
    /// Reused by the 5-step accordion sidebar.
    /// </summary>
    public static Microsoft.UI.Xaml.Media.Brush GetStepBadgeBrush(NodeStatus status) => status switch
    {
        NodeStatus.Running => new Microsoft.UI.Xaml.Media.SolidColorBrush(Converters.NodeStatusToBrushConverter.ParseHexColor("#F59E0B")),
        NodeStatus.Success => new Microsoft.UI.Xaml.Media.SolidColorBrush(Converters.NodeStatusToBrushConverter.ParseHexColor("#10B981")),
        NodeStatus.Failed  => new Microsoft.UI.Xaml.Media.SolidColorBrush(Converters.NodeStatusToBrushConverter.ParseHexColor("#EF4444")),
        _ => new Microsoft.UI.Xaml.Media.SolidColorBrush(Converters.NodeStatusToBrushConverter.ParseHexColor("#475569"))
    };

    public static string GetStepBadgeText(NodeStatus status) => status switch
    {
        NodeStatus.Running => "⏳ Đang chạy...",
        NodeStatus.Success => "✔️ Hoàn thành",
        NodeStatus.Failed  => "❌ Lỗi",
        _ => "⚪ Chờ"
    };

    public GeminiViewModel(
        GeminiCreatorService? geminiCreatorService = null,
        PipelineOrchestrator? pipelineOrchestrator = null,
        YoutubeTopicSuggestionStep? topicSuggestionStep = null,
        GeminiApiService? geminiApiService = null,
        PythonServerManager? pythonServerManager = null,
        IConfigService? configService = null,
        ILogService? logService = null,
        IBrowserService? browserService = null)
    {
        _geminiCreatorService = geminiCreatorService;
        _pipelineOrchestrator = pipelineOrchestrator;
        _topicSuggestionStep = topicSuggestionStep;
        _geminiApiService = geminiApiService;
        _pythonServerManager = pythonServerManager;
        _configService = configService;
        _logService = logService;
        _browserService = browserService;

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? (App.MainWindowInstance?.DispatcherQueue);

        if (_logService != null)
        {
            _logService.OnLogEntry += OnLogServiceEntry;
        }

        if (_geminiCreatorService != null)
        {
            GeminiTasks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSelectedTasks));
        }

        if (GeminiTasks.Count == 0 && _geminiCreatorService != null)
        {
            GeminiTasks.Add(_geminiCreatorService.CreateDefaultTask(
                AvailableScriptwriterGems,
                AvailableSceneCreatorGems,
                $"Chủ đề video mới #{GeminiTasks.Count + 1}"));
        }

        if (_geminiCreatorService != null)
        {
            _ = LoadGemsAsync();
        }
    }

    private void OnLogServiceEntry(LogEntry entry)
    {
        if (entry.Category is not (LogCategory.GeminiCreator
            or LogCategory.GeminiApi
            or LogCategory.Pipeline
            or LogCategory.CookieSync
            or LogCategory.PythonServer
            or LogCategory.General))
        {
            return;
        }

        string line = entry.Category == LogCategory.PythonServer
            ? $"[{entry.FormattedTimestamp}] [Python] {entry.Message}\n"
            : $"[{entry.FormattedTimestamp}] [{entry.Level}] {entry.Message}\n";

        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(() => ConsoleLogs += line);
        }
        else
        {
            ConsoleLogs += line;
        }
    }

    public bool HasSelectedTasks => GeminiTasks.Any(t => t.IsSelected);
    public bool HasSelectedTask => SelectedTask != null;

    // ── Safe step status/log accessors (return defaults when no task selected) ──
    public NodeStatus CurrentStep1Status => SelectedTask?.Step1Status ?? NodeStatus.Idle;
    public NodeStatus CurrentStep2Status => SelectedTask?.Step2Status ?? NodeStatus.Idle;
    public NodeStatus CurrentStep3Status => SelectedTask?.Step3Status ?? NodeStatus.Idle;
    public NodeStatus CurrentStep4Status => SelectedTask?.Step4Status ?? NodeStatus.Idle;
    public NodeStatus CurrentStep5Status => SelectedTask?.Step5Status ?? NodeStatus.Idle;

    public string CurrentStep1Log => string.IsNullOrWhiteSpace(SelectedTask?.Step1Logs) ? "Chưa có log cho bước này." : SelectedTask.Step1Logs;
    public string CurrentStep2Log => string.IsNullOrWhiteSpace(SelectedTask?.Step2Logs) ? "Chưa có log cho bước này." : SelectedTask.Step2Logs;
    public string CurrentStep3Log => string.IsNullOrWhiteSpace(SelectedTask?.Step3Logs) ? "Chưa có log cho bước này." : SelectedTask.Step3Logs;
    public string CurrentStep4Log => string.IsNullOrWhiteSpace(SelectedTask?.Step4Logs) ? "Chưa có log cho bước này." : SelectedTask.Step4Logs;
    public string CurrentStep5Log => string.IsNullOrWhiteSpace(SelectedTask?.Step5Logs) ? "Chưa có log cho bước này." : SelectedTask.Step5Logs;

    public string SelectedTaskTopic => SelectedTask?.Topic ?? "Chưa chọn task";

    partial void OnSelectedTaskChanged(GeminiTaskModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
        OnPropertyChanged(nameof(SelectedTaskTopic));
        OnPropertyChanged(nameof(CurrentStep1Status));
        OnPropertyChanged(nameof(CurrentStep2Status));
        OnPropertyChanged(nameof(CurrentStep3Status));
        OnPropertyChanged(nameof(CurrentStep4Status));
        OnPropertyChanged(nameof(CurrentStep5Status));
        OnPropertyChanged(nameof(CurrentStep1Log));
        OnPropertyChanged(nameof(CurrentStep2Log));
        OnPropertyChanged(nameof(CurrentStep3Log));
        OnPropertyChanged(nameof(CurrentStep4Log));
        OnPropertyChanged(nameof(CurrentStep5Log));
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedTasks));
    }

    partial void OnSelectAllTasksChanged(bool value)
    {
        foreach (var task in GeminiTasks)
        {
            task.IsSelected = value;
        }
        NotifySelectionChanged();
    }

    // ─────────────────────────────────────────────────────
    //  C1 — Toolbar commands
    // ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddTask()
    {
        if (_geminiCreatorService == null)
        {
            StatusLog = "⚠️ GeminiCreatorService chưa sẵn sàng. Hãy thử lại sau.";
            return;
        }

        var newTask = _geminiCreatorService.CreateDefaultTask(
            AvailableScriptwriterGems,
            AvailableSceneCreatorGems,
            $"Chủ đề video mới #{GeminiTasks.Count + 1}");
        GeminiTasks.Add(newTask);
        SelectedTask = newTask;
        StatusLog = $"[INFO] Đã thêm task mới vào hàng đợi (Tổng: {GeminiTasks.Count} tasks).";
        _logService?.Info(LogCategory.GeminiCreator, $"Added new task to queue. Total: {GeminiTasks.Count}");
    }

    [RelayCommand]
    private void DeleteSelectedTasks()
    {
        var selected = GeminiTasks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusLog = "⚠️ Vui lòng chọn ít nhất 1 task để xóa.";
            return;
        }

        foreach (var task in selected)
        {
            GeminiTasks.Remove(task);
        }

        if (SelectedTask != null && selected.Contains(SelectedTask))
        {
            SelectedTask = null;
        }

        StatusLog = $"[INFO] Đã xóa {selected.Count} task khỏi bảng.";
        _logService?.Info(LogCategory.GeminiCreator, $"Deleted {selected.Count} tasks from queue.");
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void DeleteSingleTask(GeminiTaskModel? task)
    {
        if (task == null) return;
        GeminiTasks.Remove(task);
        if (SelectedTask == task)
        {
            SelectedTask = null;
        }
        StatusLog = $"[INFO] Đã xóa task '{task.Topic}'.";
        _logService?.Info(LogCategory.GeminiCreator, $"Deleted task: {task.Topic}");
        NotifySelectionChanged();
    }

    [RelayCommand]
    private async Task RefreshGemsAsync()
    {
        if (_geminiCreatorService == null)
        {
            StatusLog = "⚠️ GeminiCreatorService chưa sẵn sàng.";
            return;
        }

        await LoadGemsAsync();
    }

    private async Task LoadGemsAsync()
    {
        if (_geminiCreatorService == null) return;

        IsLoadingGems = true;
        StatusLog = "[GEMS] 🔄 Đang tải danh sách Gemini Gems...";

        try
        {
            await _geminiCreatorService.LoadGeminiGemsAsync(
                AvailableScriptwriterGems,
                AvailableSceneCreatorGems,
                GeminiTasks,
                onStatus: msg => _dispatcherQueue.TryEnqueue(() => StatusLog = msg));
            StatusLog = $"[GEMS] ✅ Đã nạp {AvailableScriptwriterGems.Count} Gems.";
        }
        catch (Exception ex)
        {
            StatusLog = $"[GEMS] ❌ Lỗi tải Gems: {ex.Message}";
            _logService?.Error(LogCategory.GeminiCreator, $"Failed to load gems: {ex.Message}");
        }
        finally
        {
            IsLoadingGems = false;
        }
    }

    public class ChromeProfileItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    private List<ChromeProfileItem> GetAvailableChromeProfiles()
    {
        var list = new List<ChromeProfileItem>();
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddProfile(string name, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(name) || !Directory.Exists(fullPath)) return;
            if (addedNames.Add(name))
            {
                list.Add(new ChromeProfileItem { DisplayName = name, FullPath = Path.GetFullPath(fullPath) });
            }
        }

        // Primary: Configured ChromeProfilesDir (matches Chrome Profiles tab in UI)
        string profilesDir = string.Empty;
        if (_configService != null)
        {
            profilesDir = _configService.LoadSettings().ChromeProfilesDir;
        }

        if (string.IsNullOrWhiteSpace(profilesDir) || !Directory.Exists(profilesDir))
        {
            profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
        }

        if (Directory.Exists(profilesDir))
        {
            foreach (var dir in Directory.GetDirectories(profilesDir))
            {
                AddProfile(Path.GetFileName(dir), dir);
            }
        }

        // Fallback: Default GeminiProfile if list is empty
        if (list.Count == 0)
        {
            string fallbackPath = Path.Combine(profilesDir, "GeminiProfile");
            Directory.CreateDirectory(fallbackPath);
            AddProfile("GeminiProfile", fallbackPath);
        }

        return list;
    }

    [RelayCommand]
    private async Task ImportCookiesAsync()
    {
        if (_geminiCreatorService == null || _browserService == null)
        {
            StatusLog = "⚠️ Cookie import chưa sẵn sàng (thiếu GeminiCreatorService hoặc BrowserService).";
            _logService?.Error(LogCategory.CookieSync, "⚠️ Cookie import chưa sẵn sàng (thiếu GeminiCreatorService hoặc BrowserService).");
            IsConsoleLogVisible = true;
            return;
        }

        IsImportingCookies = true;
        IsConsoleLogVisible = true;

        try
        {
            if (App.MainWindowInstance?.Content?.XamlRoot != null)
            {
                var profiles = GetAvailableChromeProfiles();

                var profileCombo = new ComboBox
                {
                    Header = "Chọn Hồ Sơ Trình Duyệt (Chrome Profile):",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 4, 0, 8)
                };

                foreach (var p in profiles)
                {
                    profileCombo.Items.Add(p);
                }
                profileCombo.SelectedIndex = 0;

                var modeRadioProfile = new RadioButton
                {
                    Content = "🌐 Đăng nhập Chrome với Profile được chọn (Khuyên dùng)",
                    IsChecked = true,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                var modeRadioAuto = new RadioButton
                {
                    Content = "⚡ Tự động quét tất cả Chrome Profiles (Không mở trình duyệt)",
                    Margin = new Thickness(0, 2, 0, 2)
                };
                var modeRadioFile = new RadioButton
                {
                    Content = "📁 Chọn file cookies.json thủ công từ máy tính",
                    Margin = new Thickness(0, 2, 0, 4)
                };

                var stack = new StackPanel { Spacing = 8, Width = 400 };
                stack.Children.Add(new TextBlock
                {
                    Text = "Vui lòng chọn Chrome Profile để mở trình duyệt & nạp Cookies Gemini:",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 4)
                });
                stack.Children.Add(modeRadioProfile);
                stack.Children.Add(profileCombo);
                stack.Children.Add(modeRadioAuto);
                stack.Children.Add(modeRadioFile);

                var dialog = new ContentDialog
                {
                    Title = "🔑 Nạp / Import Cookies Gemini",
                    Content = stack,
                    PrimaryButtonText = "Bắt đầu nạp",
                    CloseButtonText = "Hủy",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = App.MainWindowInstance.Content.XamlRoot
                };

                var dialogResult = await dialog.ShowAsync();
                if (dialogResult != ContentDialogResult.Primary)
                {
                    return;
                }

                if (modeRadioProfile.IsChecked == true)
                {
                    var selectedProfile = profileCombo.SelectedItem as ChromeProfileItem;
                    await RunPlaywrightLoginAsync(selectedProfile?.FullPath);
                }
                else if (modeRadioAuto.IsChecked == true)
                {
                    _logService?.Info(LogCategory.CookieSync, "⏳ Đang tự động quét & trích xuất Cookies Gemini từ các Chrome Profiles...");
                    var (success, message) = await _geminiCreatorService.ImportCookiesAsync(
                        GeminiCreatorService.CookieImportMode.Auto,
                        _browserService,
                        onStatus: msg => _logService?.Info(LogCategory.CookieSync, msg));

                    if (success)
                    {
                        _logService?.Success(LogCategory.CookieSync, $"🎉 {message}");
                        StatusLog = $"[COOKIE] ✅ {message}";
                        await LoadGemsAsync();
                    }
                    else
                    {
                        _logService?.Warning(LogCategory.CookieSync, $"⚠️ Quét tự động thất bại: {message}");
                        StatusLog = $"[COOKIE] ⚠️ Quét tự động thất bại.";

                        var selectedProfile = profileCombo.SelectedItem as ChromeProfileItem;
                        var fallbackDialog = new ContentDialog
                        {
                            Title = "🌐 Mở Chrome Đăng Nhập Gemini",
                            Content = $"Tự động quét không tìm thấy session Gemini hợp lệ.\n({message})\n\nBạn có muốn mở Chrome với Profile '{selectedProfile?.DisplayName ?? "GeminiProfile"}' để đăng nhập Gemini không?",
                            PrimaryButtonText = "🌐 Mở Chrome ngay",
                            CloseButtonText = "Bỏ qua",
                            DefaultButton = ContentDialogButton.Primary,
                            XamlRoot = App.MainWindowInstance.Content.XamlRoot
                        };

                        if (await fallbackDialog.ShowAsync() == ContentDialogResult.Primary)
                        {
                            await RunPlaywrightLoginAsync(selectedProfile?.FullPath);
                        }
                    }
                }
                else if (modeRadioFile.IsChecked == true)
                {
                    await RunManualFileImportAsync();
                }
            }
            else
            {
                await RunPlaywrightLoginAsync(null);
            }
        }
        catch (Exception ex)
        {
            StatusLog = $"[COOKIE] ❌ Lỗi: {ex.Message}";
            _logService?.Error(LogCategory.CookieSync, $"Cookie import failed: {ex.Message}");
        }
        finally
        {
            IsImportingCookies = false;
        }
    }

    private async Task RunPlaywrightLoginAsync(string? targetProfilePath = null)
    {
        if (_geminiCreatorService == null || _browserService == null) return;

        string profileDisplayName = !string.IsNullOrEmpty(targetProfilePath)
            ? Path.GetFileName(targetProfilePath)
            : "GeminiProfile";

        _logService?.Info(LogCategory.CookieSync, $"🌐 Đang mở Chrome (Profile: {profileDisplayName}) để đăng nhập Gemini...");
        var (success, msg, profilePath) = await _geminiCreatorService.LoginViaPlaywrightAsync(
            _browserService,
            targetProfilePath,
            onStatus: s => _logService?.Info(LogCategory.CookieSync, s));

        if (success)
        {
            _logService?.Success(LogCategory.CookieSync, $"🎉 {msg}");
            StatusLog = $"[COOKIE] ✅ {msg}";
            await LoadGemsAsync();
        }
        else
        {
            _logService?.Error(LogCategory.CookieSync, $"❌ Đăng nhập Playwright thất bại: {msg}");
            StatusLog = $"[COOKIE] ❌ {msg}";
        }
    }

    private async Task RunManualFileImportAsync()
    {
        if (_geminiCreatorService == null || _browserService == null) return;

        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add("*");

            if (App.MainWindowInstance != null)
            {
                var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string content = await Windows.Storage.FileIO.ReadTextAsync(file);
                _logService?.Info(LogCategory.CookieSync, $"📁 Đang đọc file cookies: {file.Path}...");
                var (success, msg) = await _geminiCreatorService.SaveCustomCookiesAsync(
                    content,
                    _browserService,
                    onStatus: s => _logService?.Info(LogCategory.CookieSync, s));

                if (success)
                {
                    _logService?.Success(LogCategory.CookieSync, $"🎉 {msg}");
                    StatusLog = $"[COOKIE] ✅ {msg}";
                    await LoadGemsAsync();
                }
                else
                {
                    _logService?.Error(LogCategory.CookieSync, $"❌ Nạp file cookies thất bại: {msg}");
                    StatusLog = $"[COOKIE] ❌ {msg}";
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error(LogCategory.CookieSync, $"❌ Lỗi đọc file cookies: {ex.Message}");
            StatusLog = $"[COOKIE] ❌ Lỗi: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SuggestTopicsAsync()
    {
        if (_topicSuggestionStep == null || SelectedTask == null)
        {
            StatusLog = "⚠️ Vui lòng chọn 1 task trong bảng trước khi gợi ý chủ đề.";
            return;
        }

        string channelUrl = !string.IsNullOrWhiteSpace(SelectedTask.ChannelUrl)
            ? SelectedTask.ChannelUrl
            : SelectedTask.Topic;

        if (string.IsNullOrWhiteSpace(channelUrl))
        {
            StatusLog = "⚠️ Vui lòng nhập Channel URL hoặc Topic mô tả trước.";
            return;
        }

        IsSuggestingTopics = true;
        SelectedTask.IsLoadingSuggestions = true;
        StatusLog = "[SUGGEST] 💡 Đang phân tích và gợi ý chủ đề...";

        try
        {
            var response = await _topicSuggestionStep.SuggestTopicsAsync(
                channelUrl,
                SelectedTask.SelectedScriptwriterGem?.Id,
                SelectedTask.ScriptwriterModel,
                msg => _dispatcherQueue.TryEnqueue(() => StatusLog = msg));

            if (response != null && response.SuggestedTopics.Count > 0)
            {
                SelectedTask.SuggestedTopics = response.SuggestedTopics;
                StatusLog = $"[SUGGEST] ✅ Gợi ý {response.SuggestedTopics.Count} chủ đề từ '{response.ChannelName}'.";
            }
            else
            {
                StatusLog = "[SUGGEST] ⚠️ Không nhận được gợi ý. Vui lòng thử lại.";
            }
        }
        catch (Exception ex)
        {
            StatusLog = $"[SUGGEST] ❌ Lỗi: {ex.Message}";
            _logService?.Error(LogCategory.GeminiCreator, $"Topic suggestion failed: {ex.Message}");
        }
        finally
        {
            SelectedTask.IsLoadingSuggestions = false;
            IsSuggestingTopics = false;
        }
    }

    [RelayCommand]
    private async Task RunSelectedTasksAsync()
    {
        var selected = GeminiTasks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusLog = "⚠️ Vui lòng tích chọn ít nhất 1 task để thực thi.";
            IsConsoleLogVisible = true;
            await ShowRunAlertAsync("Chưa chọn task", "Vui lòng tích chọn ít nhất 1 task trong bảng để thực thi thi pipeline.");
            return;
        }

        await RunTaskBatchAsync(selected);
    }

    [RelayCommand]
    private async Task RunSingleTaskAsync(GeminiTaskModel? task)
    {
        if (task == null) return;
        await RunTaskBatchAsync(new List<GeminiTaskModel> { task });
    }

    /// <summary>
    /// Resets per-step status + logs of a task so re-running a previously failed/completed
    /// task does not carry over stale step badges ("✔️ Hoàn thành" on a step that will
    /// re-run). Mirrors WPF behaviour where a fresh run starts the accordion from "⚪ Chờ".
    /// </summary>
    private static void ResetTaskStatuses(GeminiTaskModel task)
    {
        task.Status = NodeStatus.Idle;
        task.CurrentStepInfo = "Sẵn sàng";
        task.Step1Status = NodeStatus.Idle;
        task.Step2Status = NodeStatus.Idle;
        task.Step3Status = NodeStatus.Idle;
        task.Step4Status = NodeStatus.Idle;
        task.Step5Status = NodeStatus.Idle;
        task.Step1Logs = string.Empty;
        task.Step2Logs = string.Empty;
        task.Step3Logs = string.Empty;
        task.Step4Logs = string.Empty;
        task.Step5Logs = string.Empty;
    }

    /// <summary>
    /// Opens a small InfoBar-style ContentDialog from the main window's XamlRoot.
    /// Wraps the native <see cref="ContentDialog"/> for safe invocation off the UI thread.
    /// </summary>
    private async Task ShowRunAlertAsync(string title, string message, string closeText = "Đóng")
    {
        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageFallbackXamlRoot;
        if (xamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };
        await dialog.ShowAsync();
    }

    /// <summary>Lazy-captured XamlRoot for dialogs shown before the main window exists.</summary>
    private Microsoft.UI.Xaml.XamlRoot? PageFallbackXamlRoot { get; set; }

    private async Task RunTaskBatchAsync(IList<GeminiTaskModel> tasks)
    {
        if (_pipelineOrchestrator == null || _geminiCreatorService == null)
        {
            StatusLog = "⚠️ Pipeline orchestrator chưa sẵn sàng.";
            _logService?.Error(LogCategory.GeminiCreator, "Pipeline orchestrator / GeminiCreatorService null — DI không inject.");
            IsConsoleLogVisible = true;
            return;
        }

        if (IsGenerating)
        {
            StatusLog = "⚠️ Đang có phiên chạy khác. Hãy hủy trước khi bắt đầu mới.";
            IsConsoleLogVisible = true;
            return;
        }

        if (tasks.Count == 0)
        {
            StatusLog = "⚠️ Không có task nào để chạy.";
            return;
        }

        // Reset status on every task so re-runs start from a clean slate
        foreach (var t in tasks)
        {
            ResetTaskStatuses(t);
        }

        // Mirror WPF: auto-open logs drawer for the first task so the user can see
        // per-step progress immediately without having to click the row manually.
        SelectedTask = tasks[0];
        IsTaskLogsDrawerOpen = true;
        IsConsoleLogVisible = true;

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        CancellationToken ct = _runCts.Token;

        IsGenerating = true;
        StatusLog = $"[RUN] 🚀 Bắt đầu pipeline cho {tasks.Count} task...";
        _logService?.Info(LogCategory.Pipeline,
            $"Bắt đầu batch Gemini pipeline cho {tasks.Count} task (Slot: DeepRsrch=2 | Voiceover=3 | SceneCreator=4 | ImageGen=1).");

        PipelineBatchResult? batchResult = null;

        try
        {
            string? apiKey = _configService?.CurrentSettings.Ai84ApiKey;
            foreach (var t in tasks)
            {
                if (string.IsNullOrEmpty(t.TargetLanguage) && !string.IsNullOrEmpty(t.VoiceId) && !string.IsNullOrEmpty(apiKey))
                {
                    await _geminiCreatorService.ResolveTaskLanguageAsync(t, apiKey);
                }
            }

            batchResult = await _pipelineOrchestrator.ExecuteBatchAsync(
                tasks.ToList(),
                (taskModel, msg) =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        string stamped = $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                        taskModel.Logs += stamped;
                        // Also push to the global Console Logs panel so the user can see
                        // pipeline progress even when no Logs drawer is open.
                        ConsoleLogs += stamped;
                        UpdateTaskStepInfoFromLog(taskModel, msg);
                    });
                },
                ct).ConfigureAwait(true);

            StatusLog = $"[RUN] {batchResult}";
            _logService?.Success(LogCategory.Pipeline, batchResult.ToString());
        }
        catch (OperationCanceledException)
        {
            StatusLog = "[RUN] ⏹️ Đã hủy pipeline theo yêu cầu.";
            _logService?.Warning(LogCategory.GeminiCreator, "Pipeline cancelled by user.");
        }
        catch (Exception ex)
        {
            // Surface full exception so empty `ex.Message` cases (e.g. inner
            // exceptions without a message, or thrown from non-default ctor)
            // are still diagnosable. Walks the InnerException chain and keeps
            // the stack trace so we can spot the failing step.
            //
            // AggregateException can show up when Task.Run wraps async failures.
            // Unwrap first so we always report the *real* root cause.
            Exception root = ex;
            while (root is AggregateException agg && agg.InnerException != null)
                root = agg.InnerException;

            var detail = BuildExceptionDetail(ex);
            StatusLog = $"[RUN] ❌ Lỗi pipeline: {root.GetType().Name}: {root.Message}";
            _logService?.Error(LogCategory.GeminiCreator, $"Pipeline error: {root.GetType().Name}: {root.Message}\n{detail}");
            // Also dump to ConsoleLogs so the user can copy the stack trace
            ConsoleLogs += $"[{DateTime.Now:HH:mm:ss}] [RUN] ❌ Pipeline exception ({root.GetType().Name})\n{detail}\n";

            // Try to mark per-task as failed so the UI doesn't look "stuck"
            foreach (var t in tasks)
            {
                if (t is GeminiTaskModel gm)
                {
                    gm.Step1Status = NodeStatus.Failed;
                    gm.Step2Status = NodeStatus.Failed;
                    gm.Step3Status = NodeStatus.Failed;
                    gm.Step4Status = NodeStatus.Failed;
                    gm.Step5Status = NodeStatus.Failed;
                }
            }
        }
        finally
        {
            IsGenerating = false;
            _runCts?.Dispose();
            _runCts = null;
        }

        // Mirror WPF: show summary dialog with success/failed counts + elapsed time
        if (batchResult != null)
        {
            try
            {
                var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageFallbackXamlRoot;
                if (xamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Pipeline Orchestrator — Kết Quả",
                        Content =
                            $"Đã hoàn thành thực thi {batchResult.TotalTasks} Gemini tasks!\n\n" +
                            $"✅ Thành công: {batchResult.SuccessCount}\n" +
                            $"❌ Thất bại: {batchResult.FailedCount}\n" +
                            $"⏱️ Thời gian: {batchResult.Elapsed.TotalMinutes:F1} phút",
                        CloseButtonText = "Đóng",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = xamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }
            catch
            {
                // best-effort summary — never crash the run if the dialog can't show
            }
        }
    }

    // ─────────────────────────────────────────────────────
    //  Step routing — push orchestrator log lines into the
    //  per-step accordion of the *currently selected* task.
    //  Mirrors the WPF UpdateTaskStepInfoFromLog heuristic.
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a multi-line exception diagnostics string for logging. Walks the
    /// InnerException chain and includes the stack trace so we can pinpoint
    /// the failure even when <see cref="Exception.Message"/> is empty/null.
    /// </summary>
    private static string BuildExceptionDetail(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        int depth = 0;
        var current = ex;
        while (current != null)
        {
            sb.AppendLine($"  [{depth}] {current.GetType().FullName}: {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace))
            {
                sb.AppendLine("      StackTrace:");
                sb.AppendLine(current.StackTrace);
            }
            current = current.InnerException;
            depth++;
            if (depth > 8) { sb.AppendLine("  ... (truncated)"); break; }
        }
        return sb.ToString();
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
            taskItem.CurrentStepInfo = elapsedIdx >= 0
                ? $"Step 1: Deep Research ({msg.Substring(elapsedIdx).Trim()})"
                : "Step 1: Deep Research (Đang nghiên cứu...)";
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
            // General pipeline logs fall through to whichever step is currently running
            if (taskItem.Step4Status == NodeStatus.Running) taskItem.Step4Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            else if (taskItem.Step3Status == NodeStatus.Running) taskItem.Step3Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            else if (taskItem.Step2Status == NodeStatus.Running) taskItem.Step2Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            else taskItem.Step1Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
        }

        // Notify accordion so badges/labels refresh when this task is the selected one
        // (or even if not — XAML binds via CurrentStep1..5Status which returns SelectedTask's value,
        // so when the user opens the live task drawer the badges will already reflect the latest run).
        OnPropertyChanged(nameof(CurrentStep1Status));
        OnPropertyChanged(nameof(CurrentStep2Status));
        OnPropertyChanged(nameof(CurrentStep3Status));
        OnPropertyChanged(nameof(CurrentStep4Status));
        OnPropertyChanged(nameof(CurrentStep5Status));
        OnPropertyChanged(nameof(CurrentStep1Log));
        OnPropertyChanged(nameof(CurrentStep2Log));
        OnPropertyChanged(nameof(CurrentStep3Log));
        OnPropertyChanged(nameof(CurrentStep4Log));
        OnPropertyChanged(nameof(CurrentStep5Log));
    }

    // ─────────────────────────────────────────────────────
    //  C3 — Row details Flyout actions
    // ─────────────────────────────────────────────────────

    public string GetTaskOutputDir(GeminiTaskModel task)
    {
        string folderKey = !string.IsNullOrWhiteSpace(task.OutputFolderName)
            ? task.OutputFolderName
            : (!string.IsNullOrWhiteSpace(task.Topic) ? AssetAutomator.Core.Constants.YoutubeHelper.ToSafeTopicSlug(task.Topic.Trim()) : task.Id);

        string? baseDir = _configService?.CurrentSettings?.OutputsDir;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Outputs");
        }

        return Path.Combine(baseDir, "Gemini", folderKey);
    }

    [RelayCommand]
    private void OpenTaskFolder(GeminiTaskModel? task)
    {
        if (task == null) return;

        string outputDir = GetTaskOutputDir(task);

        try
        {
            Directory.CreateDirectory(outputDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            StatusLog = $"[FOLDER] ❌ Không thể mở thư mục: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ViewTaskScenes(GeminiTaskModel? task)
    {
        if (task == null) return;

        string outputDir = GetTaskOutputDir(task);
        string scenesPath = Path.Combine(outputDir, "scenes.json");

        if (!File.Exists(scenesPath))
        {
            StatusLog = $"[SCENES] ⚠️ Không tìm thấy scenes.json tại {scenesPath}. Hãy chạy task trước.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = scenesPath,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            StatusLog = $"[SCENES] ❌ Lỗi mở file: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ApplySuggestedTopic(SuggestedTopic? topic)
    {
        if (topic == null || SelectedTask == null) return;
        SelectedTask.Topic = topic.Title;
        StatusLog = $"[SUGGEST] ✅ Đã áp dụng topic: '{topic.Title}'.";
    }

    // ─────────────────────────────────────────────────────
    //  C5 — Cancel + ClearLogs
    // ─────────────────────────────────────────────────────

    [RelayCommand]
    private void CancelGeneration()
    {
        if (!IsGenerating)
        {
            StatusLog = "ℹ️ Không có phiên nào đang chạy để hủy.";
            return;
        }

        try
        {
            _runCts?.Cancel();
            StatusLog = "[CANCEL] ⏹️ Đã gửi tín hiệu hủy tới pipeline...";
            _logService?.Warning(LogCategory.GeminiCreator, "User requested cancellation of Gemini pipeline.");
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        ConsoleLogs = string.Empty;
        StatusLog = "ℹ️ Đã xóa console logs.";
    }
}
