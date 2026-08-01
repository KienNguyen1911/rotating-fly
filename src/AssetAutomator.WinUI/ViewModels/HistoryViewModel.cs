using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly HistoryService? _historyService;

    [ObservableProperty]
    private ObservableCollection<HistoryTaskModel> _historyEntries = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableDates = new();

    [ObservableProperty]
    private string? _selectedDate;

    [ObservableProperty]
    private HistoryTaskModel? _selectedEntry;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalHistoryCount;

    [ObservableProperty]
    private string _statusText = "Nhật ký thực thi sẵn sàng";

    public HistoryViewModel(HistoryService? historyService = null)
    {
        _historyService = historyService;
        LoadHistory();
    }

    [RelayCommand]
    private void LoadHistory()
    {
        HistoryEntries.Clear();
        AvailableDates.Clear();

        if (_historyService != null)
        {
            var dates = _historyService.LoadHistoryDates();
            foreach (var date in dates)
            {
                AvailableDates.Add(date);
            }

            if (AvailableDates.Count > 0)
            {
                SelectedDate = AvailableDates[0];
                var tasks = _historyService.LoadHistoryTasksForDate(SelectedDate);
                foreach (var task in tasks)
                {
                    HistoryEntries.Add(task);
                }
            }
        }

        if (HistoryEntries.Count == 0)
        {
            // Initial fallback mock data if no history files exist yet
            HistoryEntries.Add(new HistoryTaskModel { Id = Guid.NewGuid(), VideoUrl = "https://youtube.com/watch?v=sample1", TargetLanguage = "en", VoiceId = "en-1", Status = "SUCCESS", CreatedAt = DateTime.Now.AddHours(-2), Logs = "Chạy 5 scenes hoàn tất 100%" });
            HistoryEntries.Add(new HistoryTaskModel { Id = Guid.NewGuid(), VideoUrl = "https://youtube.com/watch?v=sample2", TargetLanguage = "vi", VoiceId = "vi-1", Status = "SUCCESS", CreatedAt = DateTime.Now.AddHours(-5), Logs = "Sinh 10 ảnh thumbnail thành công" });
        }

        TotalHistoryCount = HistoryEntries.Count;
        StatusText = $"Đã nạp {TotalHistoryCount} mục nhật ký từ đĩa cứng.";
    }

    partial void OnSelectedDateChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && _historyService != null)
        {
            HistoryEntries.Clear();
            var tasks = _historyService.LoadHistoryTasksForDate(value);
            foreach (var task in tasks)
            {
                HistoryEntries.Add(task);
            }
            TotalHistoryCount = HistoryEntries.Count;
            StatusText = $"Hiển thị nhật ký cho ngày: {value} ({TotalHistoryCount} mục).";
        }
    }

    [RelayCommand]
    private void ClearHistory()
    {
        HistoryEntries.Clear();
        TotalHistoryCount = 0;
        StatusText = "Đã xóa danh sách hiển thị nhật ký.";
    }
}
