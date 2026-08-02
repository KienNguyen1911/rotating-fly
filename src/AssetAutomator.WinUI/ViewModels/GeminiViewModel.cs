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
using Microsoft.UI.Dispatching;

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
    //  C6 — Python Server Log panel
    // ─────────────────────────────────────────────────────

    [ObservableProperty]
    private string _pythonServerLog = string.Empty;

    [ObservableProperty]
    private string _pythonServerStatus = "⚪ Unknown";

    [ObservableProperty]
    private bool _isPythonLogPanelCollapsed;

    public bool IsPythonLogPanelVisible => !IsPythonLogPanelCollapsed;

    partial void OnIsPythonLogPanelCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPythonLogPanelVisible));
    }

    [RelayCommand]
    private void TogglePythonLogPanel()
    {
        IsPythonLogPanelCollapsed = !IsPythonLogPanelCollapsed;
    }

    [RelayCommand]
    private void ClearPythonServerLog()
    {
        PythonServerLog = string.Empty;
        StatusLog = "ℹ️ Đã xóa Python Server log panel.";
    }

    // ─────────────────────────────────────────────────────
    //  Layout toggles — D8 (drawer-style sidebar to free up space)
    // ─────────────────────────────────────────────────────

    /// <summary>True = right sidebar (Python log + 5-step accordion) is shown.</summary>
    [ObservableProperty]
    private bool _isRightSidebarVisible = true;

    [RelayCommand]
    private void ToggleRightSidebar()
    {
        IsRightSidebarVisible = !IsRightSidebarVisible;
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
        // Route by category: PythonServer → Python log panel, everything else → console.
        if (entry.Category == LogCategory.PythonServer)
        {
            AppendToPythonServerLog(entry);
            return;
        }

        if (entry.Category is not (LogCategory.GeminiCreator
            or LogCategory.GeminiApi
            or LogCategory.Pipeline
            or LogCategory.CookieSync
            or LogCategory.General))
        {
            return;
        }

        string line = $"[{entry.FormattedTimestamp}] [{entry.Level}] {entry.Message}\n";

        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(() => ConsoleLogs += line);
        }
        else
        {
            ConsoleLogs += line;
        }
    }

    private void AppendToPythonServerLog(LogEntry entry)
    {
        string line = $"[{entry.FormattedTimestamp}] {entry.LevelIcon} {entry.CategoryLabel} {entry.Message}";

        void Apply()
        {
            PythonServerLog += line + Environment.NewLine;
            UpdatePythonServerStatusIndicator(entry);
        }

        if (_dispatcherQueue != null)
        {
            _dispatcherQueue.TryEnqueue(Apply);
        }
        else
        {
            Apply();
        }
    }

    private void UpdatePythonServerStatusIndicator(LogEntry entry)
    {
        if (entry.Message.Contains("successfully launched", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("Health check OK", StringComparison.OrdinalIgnoreCase) ||
            entry.Message.Contains("responding", StringComparison.OrdinalIgnoreCase))
        {
            PythonServerStatus = "✅ Running";
        }
        else if (entry.Level == LogLevel.Error &&
                 (entry.Message.Contains("exited prematurely", StringComparison.OrdinalIgnoreCase) ||
                  entry.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)))
        {
            PythonServerStatus = "❌ Crashed";
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

    partial void OnSelectedTaskChanged(GeminiTaskModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedTask));
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

    [RelayCommand]
    private async Task ImportCookiesAsync()
    {
        if (_geminiCreatorService == null || _browserService == null)
        {
            StatusLog = "⚠️ Cookie import chưa sẵn sàng (thiếu BrowserService).";
            return;
        }

        IsImportingCookies = true;
        StatusLog = "[COOKIE] 🔄 Đang nạp cookies từ Chrome profiles...";

        try
        {
            var (success, message) = await _geminiCreatorService.ImportCookiesAsync(
                GeminiCreatorService.CookieImportMode.Auto,
                _browserService,
                onStatus: msg => _dispatcherQueue.TryEnqueue(() => StatusLog = msg));
            StatusLog = $"[COOKIE] {(success ? "✅" : "❌")} {message}";
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

    [RelayCommand]
    private void OpenTaskFolder(GeminiTaskModel? task)
    {
        if (task == null) return;

        string folderKey = !string.IsNullOrWhiteSpace(task.OutputFolderName)
            ? task.OutputFolderName
            : (!string.IsNullOrWhiteSpace(task.Topic) ? task.Topic.Trim() : Guid.NewGuid().ToString("N"));
        string outputDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Output",
            "Gemini",
            string.IsNullOrWhiteSpace(folderKey) ? "task" : folderKey);

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

        string folderKey = task.OutputFolderName ?? task.Topic.Trim();
        string outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Gemini", folderKey);
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
