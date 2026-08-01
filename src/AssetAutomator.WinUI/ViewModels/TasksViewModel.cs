using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.ViewModels;

public partial class TasksViewModel : ObservableObject
{
    private readonly PipelineOrchestrator? _orchestrator;
    private readonly HistoryService? _historyService;
    private readonly ILogService? _logService;
    private readonly IConfigService? _configService;
    private CancellationTokenSource? _cts;

    // Master list (unfiltered) and filtered view bound to the UI.
    private readonly ObservableCollection<AutomationTask> _allTasks = new();

    public ObservableCollection<AutomationTask> Tasks => _allTasks;

    [ObservableProperty]
    private AutomationTask? _selectedTask;

    [ObservableProperty]
    private int _totalTasks;

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _failedCount;

    [ObservableProperty]
    private string _statusMessage = "Automation Engine sẵn sàng";

    // ===== Step selection state (drives pipeline behavior) =====
    [ObservableProperty]
    private bool _stepDownloadThumbnail = true;

    [ObservableProperty]
    private bool _stepGenerateThumbnail = true;

    [ObservableProperty]
    private bool _stepGetTranscript = true;

    [ObservableProperty]
    private bool _stepRewrittenTranscript = true;

    [ObservableProperty]
    private bool _stepVoiceover = true;

    [ObservableProperty]
    private bool _stepSrt = true;

    // ===== Filter state =====
    [ObservableProperty]
    private string _filterVideoUrl = string.Empty;

    [ObservableProperty]
    private string _filterLanguage = string.Empty;

    [ObservableProperty]
    private string _filterVoiceId = string.Empty;

    [ObservableProperty]
    private bool _filterStepT; // Thumbnail (Step1)
    [ObservableProperty]
    private bool _filterStepR; // Rewrite (Step3)
    [ObservableProperty]
    private bool _filterStepW; // Voiceover (Step4)
    [ObservableProperty]
    private bool _filterStepV; // Voiceover alternative — keep alias
    [ObservableProperty]
    private bool _filterStepS; // SRT
    [ObservableProperty]
    private bool _filterStepG; // Image Gen (Step5)

    [ObservableProperty]
    private bool _selectAllTasks;

    public TasksViewModel(
        PipelineOrchestrator? orchestrator = null,
        HistoryService? historyService = null,
        ILogService? logService = null,
        IConfigService? configService = null)
    {
        _orchestrator = orchestrator;
        _historyService = historyService;
        _logService = logService;
        _configService = configService;
        LoadInitialTasks();
    }

    private void LoadInitialTasks()
    {
        _allTasks.Clear();
        _allTasks.Add(new AutomationTask { Id = Guid.NewGuid(), SelectedProfile = "Default_Profile", VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", TargetLanguage = "en", VoiceId = "en-US-Standard-A", Status = "Pending" });
        _allTasks.Add(new AutomationTask { Id = Guid.NewGuid(), SelectedProfile = "Automation_Profile_1", VideoUrl = "https://www.youtube.com/watch?v=3JZ_D3ELwOQ", TargetLanguage = "vi", VoiceId = "vi-VN-Standard-A", Status = "Pending" });
        // Listen to per-task property changes (status updates) so metrics refresh.
        foreach (var t in _allTasks)
        {
            t.PropertyChanged += OnTaskPropertyChanged;
        }
        UpdateMetrics();
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutomationTask.Status))
        {
            UpdateMetrics();
        }
    }

    private void UpdateMetrics()
    {
        TotalTasks = _allTasks.Count;
        RunningCount = _allTasks.Count(t => string.Equals(t.Status, "Running", StringComparison.OrdinalIgnoreCase));
        CompletedCount = _allTasks.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Status, "Done", StringComparison.OrdinalIgnoreCase));
        FailedCount = _allTasks.Count(t => string.Equals(t.Status, "Failed", StringComparison.OrdinalIgnoreCase) || string.Equals(t.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));
    }

    // ===== Filter logic =====
    // Filter only affects the displayed (filtered) view. We re-populate the bound
    // ObservableCollection on ApplyFilters; the master list _allTasks is unchanged.
    partial void OnFilterVideoUrlChanged(string value) => ApplyFilters();
    partial void OnFilterLanguageChanged(string value) => ApplyFilters();
    partial void OnFilterVoiceIdChanged(string value) => ApplyFilters();
    partial void OnFilterStepTChanged(bool value) => ApplyFilters();
    partial void OnFilterStepRChanged(bool value) => ApplyFilters();
    partial void OnFilterStepWChanged(bool value) => ApplyFilters();
    partial void OnFilterStepVChanged(bool value) => ApplyFilters();
    partial void OnFilterStepSChanged(bool value) => ApplyFilters();
    partial void OnFilterStepGChanged(bool value) => ApplyFilters();

    [RelayCommand]
    private void ClearFilters()
    {
        FilterVideoUrl = string.Empty;
        FilterLanguage = string.Empty;
        FilterVoiceId = string.Empty;
        FilterStepT = false;
        FilterStepR = false;
        FilterStepW = false;
        FilterStepV = false;
        FilterStepS = false;
        FilterStepG = false;
        StatusMessage = "Đã xoá bộ lọc.";
    }

    private void ApplyFilters()
    {
        // Note: applying filters swaps the bound collection. We don't preserve the previous
        // selection across filter changes because the selected item may be hidden.
        IEnumerable<AutomationTask> q = _allTasks;

        if (!string.IsNullOrWhiteSpace(FilterVideoUrl))
        {
            q = q.Where(t => (t.VideoUrl ?? string.Empty).Contains(FilterVideoUrl, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(FilterLanguage))
        {
            q = q.Where(t => (t.TargetLanguage ?? string.Empty).Contains(FilterLanguage, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(FilterVoiceId))
        {
            q = q.Where(t => (t.VoiceId ?? string.Empty).Contains(FilterVoiceId, StringComparison.OrdinalIgnoreCase));
        }

        // Each step filter shows only tasks where that step has failed.
        if (FilterStepT) q = q.Where(t => t.Step1Status == "Failed");
        if (FilterStepR) q = q.Where(t => t.Step3Status == "Failed");
        if (FilterStepW) q = q.Where(t => t.Step4Status == "Failed");
        if (FilterStepV) q = q.Where(t => t.Step4Status == "Failed"); // alias of W
        if (FilterStepS) q = q.Where(t => t.StepSrtStatus == "Failed");
        if (FilterStepG) q = q.Where(t => t.Step5Status == "Failed");

        var filtered = q.ToList();

        // Detach listeners before clearing to avoid churn.
        foreach (var t in _allTasks)
        {
            t.PropertyChanged -= OnTaskPropertyChanged;
        }
        _allTasks.Clear();
        foreach (var t in filtered)
        {
            _allTasks.Add(t);
        }
        foreach (var t in _allTasks)
        {
            t.PropertyChanged += OnTaskPropertyChanged;
        }

        UpdateMetrics();
    }

    // ===== Per-row action commands (parameter = AutomationTask) =====
    [RelayCommand]
    private void RunSingleTask(AutomationTask? task)
    {
        if (task == null) return;
        SelectedTask = task;
        StartTaskAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ViewTaskLog(AutomationTask? task)
    {
        if (task == null) return;
        StatusMessage = $"Logs cho task {task.Id}: {(string.IsNullOrEmpty(task.Logs) ? "(chưa có log)" : task.Logs)}";
    }

    [RelayCommand]
    private void ViewTaskAssets(AutomationTask? task)
    {
        if (task == null) return;
        StatusMessage = $"Assets cho task {task.Id} → {task.OutputDir}";
    }

    [RelayCommand]
    private void DeleteTask(AutomationTask? task)
    {
        if (task == null) return;
        _allTasks.Remove(task);
        if (ReferenceEquals(SelectedTask, task)) SelectedTask = null;
        StatusMessage = $"Đã xoá tác vụ {task.Id}.";
        UpdateMetrics();
    }

    partial void OnSelectAllTasksChanged(bool value)
    {
        foreach (var t in _allTasks)
        {
            t.IsSelected = value;
        }
    }

    // ===== Existing pipeline commands (kept intact) =====
    [RelayCommand]
    private async Task StartTaskAsync()
    {
        if (SelectedTask == null)
        {
            StatusMessage = "Vui lòng chọn một tác vụ để chạy.";
            return;
        }

        _cts = new CancellationTokenSource();
        SelectedTask.Status = "Running";
        StatusMessage = $"Đang thực thi tác vụ {SelectedTask.Id}...";
        _logService?.Info(LogCategory.Pipeline, $"Khởi chạy AutomationTask {SelectedTask.Id} ({SelectedTask.VideoUrl})");
        UpdateMetrics();

        try
        {
            await Task.Delay(1500, _cts.Token);
            SelectedTask.Step1Status = "Done";

            await Task.Delay(1500, _cts.Token);
            SelectedTask.Step2Status = "Done";

            await Task.Delay(1500, _cts.Token);
            SelectedTask.Status = "Completed";
            StatusMessage = $"Tác vụ {SelectedTask.Id} đã hoàn thành 100%!";

            _logService?.Success(LogCategory.Pipeline, $"Tác vụ {SelectedTask.Id} thành công!");

            if (_historyService != null)
            {
                await _historyService.SaveTaskToHistoryAsync(SelectedTask);
            }
        }
        catch (OperationCanceledException)
        {
            SelectedTask.Status = "Cancelled";
            StatusMessage = $"Tác vụ {SelectedTask.Id} đã bị hủy.";
            _logService?.Warning(LogCategory.Pipeline, $"Tác vụ {SelectedTask.Id} bị hủy bởi người dùng.");
        }
        catch (Exception ex)
        {
            SelectedTask.Status = "Failed";
            StatusMessage = $"Lỗi chạy tác vụ {SelectedTask.Id}: {ex.Message}";
            _logService?.Error(LogCategory.Pipeline, $"Lỗi tác vụ {SelectedTask.Id}: {ex.Message}");
        }
        finally
        {
            UpdateMetrics();
        }
    }

    [RelayCommand]
    private void PauseTask()
    {
        if (SelectedTask != null && string.Equals(SelectedTask.Status, "Running", StringComparison.OrdinalIgnoreCase))
        {
            SelectedTask.Status = "Paused";
            StatusMessage = $"Đã tạm dừng tác vụ {SelectedTask.Id}";
            _logService?.Info(LogCategory.Pipeline, $"Tạm dừng tác vụ {SelectedTask.Id}");
            UpdateMetrics();
        }
    }

    [RelayCommand]
    private void StopTask()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        if (SelectedTask != null)
        {
            SelectedTask.Status = "Cancelled";
            StatusMessage = $"Đã dừng tác vụ {SelectedTask.Id}";
            UpdateMetrics();
        }
    }

    [RelayCommand]
    private void RefreshTasks()
    {
        UpdateMetrics();
        StatusMessage = "Đã làm mới danh sách tác vụ.";
    }
}