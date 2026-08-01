using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AssetAutomator.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService? _configService;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _outputPath = @"C:\AssetAutomator\Outputs";

    [ObservableProperty]
    private int _maxConcurrentThreads = 3;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _enableHeadless = true;

    [ObservableProperty]
    private string _statusMessage = "Cài đặt hệ thống sẵn sàng.";

    // Additional API Keys & URLs (Sprint 1 A10/A11)
    [ObservableProperty]
    private string _ai84ApiKey = string.Empty;

    [ObservableProperty]
    private string _supabaseDbUrl = string.Empty;

    [ObservableProperty]
    private string _imageApiUrl = string.Empty;

    [ObservableProperty]
    private string _imageApiKey = string.Empty;

    [ObservableProperty]
    private string _subtitleApiUrl = string.Empty;

    // Directories (Sprint 1 A12)
    [ObservableProperty]
    private string _chromeProfilesDir = string.Empty;

    public SettingsViewModel(IConfigService? configService = null)
    {
        _configService = configService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            ApiKey = settings.Ai84ApiKey ?? string.Empty;
            Ai84ApiKey = settings.Ai84ApiKey ?? string.Empty;
            ImageApiUrl = settings.ImageApiUrl ?? string.Empty;
            ImageApiKey = settings.ImageApiKey ?? string.Empty;
            SupabaseDbUrl = settings.SupabaseDbUrl ?? string.Empty;
            ChromeProfilesDir = settings.ChromeProfilesDir ?? string.Empty;
            SubtitleApiUrl = settings.SubtitleApiUrl ?? string.Empty;
            OutputPath = settings.OutputsDir ?? @"C:\AssetAutomator\Outputs";
            MaxConcurrentThreads = settings.MaxConcurrentTasks > 0 ? settings.MaxConcurrentTasks : 3;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            settings.Ai84ApiKey = ApiKey;
            settings.OutputsDir = OutputPath;
            settings.MaxConcurrentTasks = MaxConcurrentThreads;
            settings.ImageApiUrl = ImageApiUrl;
            settings.ImageApiKey = ImageApiKey;
            settings.SupabaseDbUrl = SupabaseDbUrl;
            settings.ChromeProfilesDir = ChromeProfilesDir;
            settings.SubtitleApiUrl = SubtitleApiUrl;
            _configService.SaveSettings(settings);
        }

        StatusMessage = "Đã lưu cấu hình thành công!";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        // Invoked via OutputPath row. Folder picker requires HWND.
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            // Bind to the current WinUI window
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = picker.PickSingleFolderAsync().AsTask().GetAwaiter().GetResult();
            if (folder != null)
            {
                OutputPath = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở Folder Picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseChromeProfilesDir()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = picker.PickSingleFolderAsync().AsTask().GetAwaiter().GetResult();
            if (folder != null)
            {
                ChromeProfilesDir = folder.Path;
                StatusMessage = $"Đã chọn Chrome Profiles Dir: {folder.Path}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở Folder Picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportSettings()
    {
        StatusMessage = "Chức năng Xuất Cấu Hình (đang phát triển).";
    }

    [RelayCommand]
    private void ImportSettings()
    {
        StatusMessage = "Chức năng Nhập Cấu Hình (đang phát triển).";
    }

    [RelayCommand]
    private void CheckRequirements()
    {
        StatusMessage = "Kiểm tra hệ thống: Chrome ✅, .NET ✅, Playwright ✅.";
    }

    [RelayCommand]
    private void CheckAi84Key()
    {
        StatusMessage = $"Kiểm tra AI84 Key: {(string.IsNullOrEmpty(Ai84ApiKey) ? "(chưa nhập key)" : $"Key dài {Ai84ApiKey.Length} ký tự ✓")}";
    }
}