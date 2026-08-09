using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.Views.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.ViewModels;

/// <summary>
/// Group header cho CollectionViewSource.GroupBy theo date category.
/// Group name đã được localize sang tiếng Việt ("Hôm nay" / "Hôm qua" / ...).
///
/// QUAN TRỌNG: Property tên <c>Items</c> (không phải <c>Entries</c>) vì WinUI
/// <c>ListView.GroupStyle</c> mặc định extract items từ property có tên đúng
/// là <c>Items</c>. Nếu không, DataContext cho từng row sẽ là Group object
/// (không phải TaskRunHistoryEntry) → mọi binding fail → row render trống.
/// Property <see cref="Entries"/> giữ làm alias cho code cũ (nếu có).
/// </summary>
public class HistoryDateGroup
{
    public string GroupName { get; set; } = string.Empty;
    /// <summary>Date chính (00:00:00 của ngày đó) — dùng để sort.</summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Items trong group. Tên phải là <c>Items</c> (not <c>Entries</c>) để
    /// WinUI GroupStyle tự động extract được. Mutable (không read-only) để
    /// cho phép gán từ object initializer.
    /// </summary>
    public ObservableCollection<TaskRunHistoryEntry> Items { get; set; } = new();

    /// <summary>Alias backward-compat cho code cũ đã reference <c>Entries</c>.</summary>
    public ObservableCollection<TaskRunHistoryEntry> Entries => Items;
}

/// <summary>
/// Wrapper cho ComboBox item trong filter "Loại task".
/// Bind trực tiếp <see cref="Value"/> vào <c>TaskTypeFilter</c> property.
/// </summary>
public class TaskTypeFilterOption
{
    public HistoryTaskType? Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;

    public TaskTypeFilterOption(HistoryTaskType? value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>
/// History tab v2 — load từ SQLite store, group theo date category,
/// hỗ trợ filter theo task type, search, sort, mở output folder, view assets, xóa entry.
///
/// Backward-compat:
///   - Vẫn inject <see cref="HistoryService"/> qua DI (giống v1).
///   - Các legacy API (<c>LoadHistoryDates</c>, <c>LoadHistoryTasksForDate</c>) vẫn hoạt động
///     nhưng không dùng — UI mới bind thẳng vào <see cref="RunGroups"/>.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService? _historyService;

    /// <summary>
    /// Grouped history: mỗi group là 1 date category (Hôm nay / Hôm qua / Tuần này / Trước đó).
    /// Bind thẳng vào <c>CollectionViewSource</c> trong XAML.
    /// </summary>
    public ObservableCollection<HistoryDateGroup> RunGroups { get; } = new();

    [ObservableProperty]
    private HistoryDateGroup? _selectedGroup;

    [ObservableProperty]
    private TaskRunHistoryEntry? _selectedEntry;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Null = "All". Filter theo task type.</summary>
    [ObservableProperty]
    private HistoryTaskType? _taskTypeFilter;

    /// <summary>True = sort StartedAt ASC (cũ nhất trước), false = DESC (mới nhất trước, mặc định).</summary>
    [ObservableProperty]
    private bool _sortOldestFirst;

    [ObservableProperty]
    private int _totalHistoryCount;

    [ObservableProperty]
    private string _statusText = "Nhật ký thực thi sẵn sàng";

    public IReadOnlyList<TaskTypeFilterOption> TaskTypeFilterOptions { get; } = new[]
    {
        new TaskTypeFilterOption(null, "🔍 Tất cả loại task"),
        new TaskTypeFilterOption(HistoryTaskType.FullPipeline, "🚀 Full Pipeline"),
        new TaskTypeFilterOption(HistoryTaskType.BatchImageGen, "🖼️ Batch Image Gen"),
        new TaskTypeFilterOption(HistoryTaskType.GeminiWriter, "✍️ Gemini Writer"),
        new TaskTypeFilterOption(HistoryTaskType.GeminiScenesCreator, "🎬 Gemini Scenes"),
        new TaskTypeFilterOption(HistoryTaskType.Manual, "📝 Manual"),
    };

    public string TaskTypeFilterDisplay(HistoryTaskType? t) => t switch
    {
        null => "🔍 Tất cả loại task",
        HistoryTaskType.FullPipeline => "🚀 Full Pipeline",
        HistoryTaskType.BatchImageGen => "🖼️ Batch Image Gen",
        HistoryTaskType.GeminiWriter => "✍️ Gemini Writer",
        HistoryTaskType.GeminiScenesCreator => "🎬 Gemini Scenes",
        _ => t.ToString() ?? "Unknown",
    };

    /// <summary>Internal partial để CommunityToolkit sinh setter notification.</summary>
    [ObservableProperty]
    private TaskTypeFilterOption? _selectedTaskTypeOption;

    public HistoryViewModel(HistoryService? historyService = null)
    {
        _historyService = historyService;
        _ = LoadHistoryAsync();
    }

    // ─────────────────────────────────────────────────────
    //  Reload + filter pipeline
    // ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        if (_historyService == null)
        {
            StatusText = "⚠️ HistoryService chưa được inject.";
            return;
        }

        try
        {
            StatusText = "⏳ Đang tải lịch sử từ SQLite...";
            var allEntries = await _historyService.LoadAllRunsAsync();
            ApplyFiltersAndGroup(allEntries);
            StatusText = $"✅ Đã nạp {TotalHistoryCount} mục nhật ký.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Lỗi tải history: {ex.Message}";
        }
    }

    partial void OnSearchQueryChanged(string value) => ReloadFromCache();
    partial void OnTaskTypeFilterChanged(HistoryTaskType? value) => ReloadFromCache();
    partial void OnSortOldestFirstChanged(bool value) => ReloadFromCache();

    partial void OnSelectedTaskTypeOptionChanged(TaskTypeFilterOption? value)
    {
        // Map wrapper → enum value, sau đó trigger filter reload.
        TaskTypeFilter = value?.Value;
    }

    private List<TaskRunHistoryEntry> _lastLoadedEntries = new();
    private void ReloadFromCache()
    {
        ApplyFiltersAndGroup(_lastLoadedEntries);
    }

    private void ApplyFiltersAndGroup(IReadOnlyList<TaskRunHistoryEntry> entries)
    {
        _lastLoadedEntries = entries.ToList();

        // 1. Filter theo search query (case-insensitive, match project name + logs summary)
        var filtered = entries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            filtered = filtered.Where(e =>
                (e.ProjectName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.LogsSummary?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (e.ErrorMessage?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // 2. Filter theo task type
        if (TaskTypeFilter.HasValue)
        {
            filtered = filtered.Where(e => e.TaskType == TaskTypeFilter.Value);
        }

        // 3. Sort theo StartedAt (toggle ASC/DESC)
        var sorted = SortOldestFirst
            ? filtered.OrderBy(e => e.StartedAt).ToList()
            : filtered.OrderByDescending(e => e.StartedAt).ToList();

        // 4. Group theo date category
        var today = DateTime.Today;
        var groups = sorted
            .GroupBy(e => ClassifyDateCategory(e.StartedAt, today))
            .OrderBy(g => g.Key.SortOrder)
            .Select(g => new HistoryDateGroup
            {
                GroupName = g.Key.DisplayName,
                Date = g.Key.Date,
                // Phải gán vào property tên "Items" (không phải "Entries") để
                // WinUI ListView.GroupStyle tự động extract items từ đây.
                Items = new ObservableCollection<TaskRunHistoryEntry>(g.OrderByDescending(e => e.StartedAt)),
            })
            .ToList();

        RunGroups.Clear();
        foreach (var g in groups) RunGroups.Add(g);

        TotalHistoryCount = sorted.Count;
        StatusText = TotalHistoryCount == 0
            ? "Không có mục nào khớp với bộ lọc hiện tại."
            : $"Đang hiển thị {TotalHistoryCount} mục (đã lọc từ {entries.Count} tổng).";
    }

    /// <summary>
    /// Phân loại 1 timestamp vào 1 date category bucket.
    /// Trả về tuple (displayName, sortOrder, date) để group có thể sort nhất quán
    /// bất kể user có chọn sortOldestFirst hay không.
    /// </summary>
    private static (string DisplayName, int SortOrder, DateTime Date) ClassifyDateCategory(DateTime startedAt, DateTime today)
    {
        DateTime entryDate = startedAt.Date;
        int daysDiff = (int)(today - entryDate).TotalDays;

        if (daysDiff == 0) return ("📅 Hôm nay", 0, entryDate);
        if (daysDiff == 1) return ("📅 Hôm qua", 1, entryDate);
        if (daysDiff <= 7) return ("📅 Tuần này", 2, entryDate);
        if (daysDiff <= 30) return ("📅 Tháng này", 3, entryDate);
        return ("📅 Trước đó", 4, entryDate);
    }

    // ─────────────────────────────────────────────────────
    //  Per-entry actions
    // ─────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenOutputFolder(TaskRunHistoryEntry? entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.OutputDirectory)) return;
        try
        {
            if (!Directory.Exists(entry.OutputDirectory))
            {
                Directory.CreateDirectory(entry.OutputDirectory);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.OutputDirectory,
                UseShellExecute = true,
                Verb = "open",
            });
            StatusText = $"📂 Đã mở folder: {entry.OutputDirectory}";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Không thể mở folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ViewAssetsAsync(TaskRunHistoryEntry? entry)
    {
        if (entry == null) return;

        // Lấy output dir từ entry — popup AssetViewerDialog sẽ đọc scenes.json/transcript.txt/srt
        string? outputDir = entry.OutputDirectory;

        // Nếu entry không có output_dir nhưng có asset_paths, dùng parent dir của asset đầu tiên
        if (string.IsNullOrWhiteSpace(outputDir) && entry.AssetPaths.Count > 0)
        {
            try
            {
                outputDir = Path.GetDirectoryName(entry.AssetPaths[0]);
            }
            catch { /* ignore */ }
        }

        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
        if (xamlRoot == null)
        {
            StatusText = "⚠️ Không tìm thấy XamlRoot để hiển thị popup.";
            return;
        }

        try
        {
            var dialog = new AssetViewerDialog(outputDir) { XamlRoot = xamlRoot };
            dialog.ApplyScreenSizedLayout();
            await dialog.ShowAsync();
            StatusText = $"📦 Đã mở Assets cho '{entry.ProjectName}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Lỗi mở Assets popup: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(TaskRunHistoryEntry? entry)
    {
        if (entry == null || _historyService == null) return;

        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
        if (xamlRoot != null)
        {
            var confirm = new ContentDialog
            {
                Title = "Xóa mục lịch sử?",
                Content = $"Bạn có chắc muốn xóa '{entry.ProjectName}' khỏi lịch sử?\n\nHành động này không thể hoàn tác.",
                PrimaryButtonText = "Xóa",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
        }

        try
        {
            await _historyService.DeleteRunAsync(entry.Id);
            await LoadHistoryAsync();
            StatusText = $"🗑️ Đã xóa entry '{entry.ProjectName}'.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Lỗi xóa entry: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        if (_historyService == null) return;

        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot;
        if (xamlRoot != null)
        {
            var confirm = new ContentDialog
            {
                Title = "Xóa toàn bộ lịch sử?",
                Content = $"Bạn sắp xóa TẤT CẢ {_lastLoadedEntries.Count} mục lịch sử.\n\nHành động này không thể hoàn tác.",
                PrimaryButtonText = "Xóa hết",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot,
            };
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
        }

        try
        {
            await _historyService.ClearAllRunsAsync();
            await LoadHistoryAsync();
            StatusText = "🗑️ Đã xóa toàn bộ lịch sử.";
        }
        catch (Exception ex)
        {
            StatusText = $"❌ Lỗi clear all: {ex.Message}";
        }
    }
}
