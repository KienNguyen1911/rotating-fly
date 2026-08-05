using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.ViewModels;

public partial class PoolViewModel : ObservableObject
{
    private readonly ImagePoolService? _poolService;
    private readonly IConfigService? _configService;

    [ObservableProperty]
    private ObservableCollection<BatchImageItem> _imageItems = new();

    [ObservableProperty]
    private BatchImageItem? _selectedItem;

    // Master list (unfiltered) — populated by RefreshPool.
    private readonly System.Collections.Generic.List<BatchImageItem> _allItems = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [ObservableProperty]
    private int _totalImagesCount;

    [ObservableProperty]
    private string _statusText = "Kho ảnh sẵn sàng";

    // Pool metric cards
    [ObservableProperty]
    private int _runningWorkers;

    [ObservableProperty]
    private int _maxWorkers;

    [ObservableProperty]
    private int _waitingRequests;

    [ObservableProperty]
    private int _processingRequests;

    [ObservableProperty]
    private int _finishedRequests;

    [ObservableProperty]
    private double _avgTimeSeconds;

    public PoolViewModel(ImagePoolService? poolService = null, IConfigService? configService = null)
    {
        _poolService = poolService;
        _configService = configService;

        if (_poolService != null)
        {
            _poolService.OnPoolStateChanged += PoolService_OnPoolStateChanged;
        }

        RefreshPool();
    }

    private void PoolService_OnPoolStateChanged()
    {
        if (_poolService != null)
        {
            var stats = _poolService.GetPoolStats();
            RunningWorkers = stats.ActiveWorkers;
            MaxWorkers = stats.MaxWorkers;
            WaitingRequests = stats.Waiting;
            ProcessingRequests = stats.Processing;
            FinishedRequests = stats.Finished;
            AvgTimeSeconds = stats.AvgSeconds;
            StatusText = $"Luồng đang chạy: {stats.ActiveWorkers}/{stats.MaxWorkers} | Đang chờ: {stats.Waiting} | Hoàn thành: {stats.Finished}";
        }
    }

    [RelayCommand]
    private void RefreshPool()
    {
        _allItems.Clear();

        if (_poolService != null)
        {
            var requests = _poolService.GetOrderedRequests();
            foreach (var req in requests)
            {
                _allItems.Add(new BatchImageItem
                {
                    Prompt = req.Prompt,
                    Status = req.Status,
                    ImagePath = req.ImagePath ?? string.Empty,
                    Engine = "Flow",
                    SceneTitle = $"Task-{req.TaskId}"
                });
            }
        }

        if (_allItems.Count == 0)
        {
            // Scan output folder for images
            string outputDir = _configService?.LoadSettings()?.OutputsDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Outputs");
            if (Directory.Exists(outputDir))
            {
                var files = Directory.GetFiles(outputDir, "*.png", SearchOption.AllDirectories).Take(20);
                foreach (var file in files)
                {
                    _allItems.Add(new BatchImageItem
                    {
                        Prompt = Path.GetFileNameWithoutExtension(file),
                        ImagePath = file,
                        Engine = "Flow Local",
                        Status = "Done"
                    });
                }
            }
        }

        if (_allItems.Count == 0)
        {
            _allItems.Add(new BatchImageItem { Prompt = "Futuristic neon city at night, 8k wallpaper", ImagePath = "sample_city.png", Engine = "flow", Status = "Done" });
            _allItems.Add(new BatchImageItem { Prompt = "Ancient dragon soaring over misty mountains", ImagePath = "sample_dragon.png", Engine = "flow", Status = "Done" });
        }

        TotalImagesCount = _allItems.Count;
        StatusText = $"Đã làm mới kho ảnh ({TotalImagesCount} mục trên đĩa).";

        // Re-apply current filter
        ApplyFilter();

        // Refresh metrics too
        PoolService_OnPoolStateChanged();
    }

    private void ApplyFilter()
    {
        IEnumerable<BatchImageItem> q = _allItems;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var qLower = SearchQuery.Trim();
            q = q.Where(item =>
                (item.Prompt ?? string.Empty).Contains(qLower, StringComparison.OrdinalIgnoreCase)
                || (item.SceneTitle ?? string.Empty).Contains(qLower, StringComparison.OrdinalIgnoreCase)
                || (item.ImagePath ?? string.Empty).Contains(qLower, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = q.ToList();
        ImageItems.Clear();
        foreach (var item in filtered)
        {
            ImageItems.Add(item);
        }
    }

    [RelayCommand]
    private void ImportImage()
    {
        StatusText = "Chức năng thêm ảnh mới từ tệp đĩa.";
    }

    [RelayCommand]
    private void DeleteImage()
    {
        if (SelectedItem != null)
        {
            if (!string.IsNullOrWhiteSpace(SelectedItem.ImagePath) && File.Exists(SelectedItem.ImagePath))
            {
                try { File.Delete(SelectedItem.ImagePath); } catch { }
            }
            _allItems.Remove(SelectedItem);
            ImageItems.Remove(SelectedItem);
            TotalImagesCount = _allItems.Count;
            StatusText = "Đã xóa ảnh được chọn khỏi kho lưu trữ.";
        }
    }
}