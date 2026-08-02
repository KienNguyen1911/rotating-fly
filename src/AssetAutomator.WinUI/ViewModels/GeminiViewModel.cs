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

    private async Task RunTaskBatchAsync(IList<GeminiTaskModel> tasks)
    {
        if (_pipelineOrchestrator == null || _geminiCreatorService == null)
        {
            StatusLog = "⚠️ Pipeline orchestrator chưa sẵn sàng.";
            return;
        }

        if (IsGenerating)
        {
            StatusLog = "⚠️ Đang có phiên chạy khác. Hãy hủy trước khi bắt đầu mới.";
            return;
        }

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        CancellationToken ct = _runCts.Token;

        IsGenerating = true;
        StatusLog = $"[RUN] 🚀 Bắt đầu pipeline cho {tasks.Count} task...";

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

            await Task.Run(() => _pipelineOrchestrator.ExecuteBatchAsync(
                tasks.ToList(),
                (taskModel, msg) =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        taskModel.Logs += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                    });
                },
                ct));
        }
        catch (OperationCanceledException)
        {
            StatusLog = "[RUN] ⏹️ Đã hủy pipeline theo yêu cầu.";
            _logService?.Warning(LogCategory.GeminiCreator, "Pipeline cancelled by user.");
        }
        catch (Exception ex)
        {
            StatusLog = $"[RUN] ❌ Lỗi pipeline: {ex.Message}";
            _logService?.Error(LogCategory.GeminiCreator, $"Pipeline error: {ex.Message}");
        }
        finally
        {
            IsGenerating = false;
            _runCts?.Dispose();
            _runCts = null;
        }
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
