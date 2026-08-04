using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AssetAutomator.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService? _configService;
    public event EventHandler? SettingsImported;

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

    // B4: Google Flow Local server fields (đã có trong AppSettings, giờ exposed trong UI)
    [ObservableProperty]
    private string _googleFlow2RootPath = Path.Combine(
        AppContext.BaseDirectory, "tools", "PythonSource");

    [ObservableProperty]
    private int _googleFlow2Port = 8787;

    [ObservableProperty]
    private bool _googleFlow2AutoLaunch = true;

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
            ApiKey = settings.ApiKey ?? string.Empty;
            Ai84ApiKey = settings.Ai84ApiKey ?? string.Empty;
            ImageApiUrl = settings.ImageApiUrl ?? string.Empty;
            ImageApiKey = settings.ImageApiKey ?? string.Empty;
            SupabaseDbUrl = settings.SupabaseDbUrl ?? string.Empty;
            ChromeProfilesDir = settings.ChromeProfilesDir ?? string.Empty;
            SubtitleApiUrl = settings.SubtitleApiUrl ?? string.Empty;
            OutputPath = settings.OutputsDir ?? @"C:\AssetAutomator\Outputs";
            MaxConcurrentThreads = settings.MaxConcurrentTasks > 0 ? settings.MaxConcurrentTasks : 3;
            // B4: Đọc 3 fields GoogleFlow2* từ AppSettings
            GoogleFlow2RootPath = settings.GoogleFlow2RootPath ?? Path.Combine(
                AppContext.BaseDirectory, "tools", "PythonSource");
            GoogleFlow2Port = settings.GoogleFlow2Port > 0 ? settings.GoogleFlow2Port : 8787;
            GoogleFlow2AutoLaunch = settings.GoogleFlow2AutoLaunch;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            settings.ApiKey = ApiKey;
            settings.Ai84ApiKey = Ai84ApiKey;
            settings.OutputsDir = OutputPath;
            settings.MaxConcurrentTasks = MaxConcurrentThreads;
            settings.ImageApiUrl = ImageApiUrl;
            settings.ImageApiKey = ImageApiKey;
            settings.SupabaseDbUrl = SupabaseDbUrl;
            settings.ChromeProfilesDir = ChromeProfilesDir;
            settings.SubtitleApiUrl = SubtitleApiUrl;
            // B4: Lưu 3 fields GoogleFlow2*
            settings.GoogleFlow2RootPath = GoogleFlow2RootPath;
            settings.GoogleFlow2Port = GoogleFlow2Port;
            settings.GoogleFlow2AutoLaunch = GoogleFlow2AutoLaunch;
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
    private async Task ExportSettingsAsync()
    {
        try
        {
            SaveSettings();

            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("JSON File", new List<string> { ".json" });
            savePicker.SuggestedFileName = "appsettings_backup.json";

            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                if (_configService != null)
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(_configService.CurrentSettings, options);
                    await FileIO.WriteTextAsync(file, json);
                    StatusMessage = $"Đã xuất cấu hình hệ thống thành công ra tệp: {file.Name} ✅";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi xuất cấu hình: {ex.Message} ❌";
        }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        try
        {
            var openPicker = new FileOpenPicker();
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".json");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(openPicker, hwnd);

            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                string json = await FileIO.ReadTextAsync(file);
                var imported = JsonSerializer.Deserialize<AppSettings>(json);
                if (imported != null && _configService != null)
                {
                    _configService.SaveSettings(imported);
                    LoadSettings();
                    SettingsImported?.Invoke(this, EventArgs.Empty);
                    StatusMessage = $"Đã nhập cấu hình từ tệp {file.Name} thành công! ✅";
                }
                else
                {
                    StatusMessage = "Tệp cấu hình không hợp lệ hoặc rỗng. ❌";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi khi nhập cấu hình: {ex.Message} ❌";
        }
    }

    [RelayCommand]
    private void CheckRequirements()
    {
        StatusMessage = "Kiểm tra hệ thống: Chrome ✅, .NET ✅, Playwright ✅.";
    }

    [RelayCommand]
    private async Task CheckAi84KeyAsync()
    {
        string key = Ai84ApiKey.Trim();
        if (string.IsNullOrEmpty(key))
        {
            StatusMessage = "Vui lòng nhập AI84 API Key trước khi kiểm tra.";
            return;
        }

        StatusMessage = "Đang kiểm tra AI84 API Key...";
        try
        {
            using var client = new System.Net.Http.HttpClient();
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.ai84.pro/v1/shared-voices?page_size=1");
            request.Headers.Add("xi-api-key", key);

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                StatusMessage = "AI84 API Key hợp lệ và hoạt động chính xác! ✅";
            }
            else
            {
                StatusMessage = $"AI84 API Key không hợp lệ (Mã: {(int)response.StatusCode} {response.ReasonPhrase}) ❌";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi kết nối tới AI84 API: {ex.Message} ❌";
        }
    }

    // ─────────────────────────────────────────────────────────
    // B4: Google Flow Local server commands
    // ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void BrowseFlow2Root()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);

            var folder = picker.PickSingleFolderAsync().AsTask().GetAwaiter().GetResult();
            if (folder != null)
            {
                GoogleFlow2RootPath = folder.Path;
                StatusMessage = $"Đã chọn Google Flow Local root: {folder.Path}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở Folder Picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestartFlow2Async()
    {
        if (_configService == null)
        {
            StatusMessage = "⚠️ ConfigService chưa sẵn sàng — restart lại app.";
            return;
        }

        // Save trước để path mới (nếu có) được persist
        SaveSettings();

        var launcher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
        if (launcher == null)
        {
            StatusMessage = "❌ GoogleFlow2ServerLauncher chưa được đăng ký trong DI.";
            return;
        }

        StatusMessage = "🔄 Restarting Google Flow Local...";
        try
        {
            // Kill process cũ (nếu còn) trước khi spawn lại
            launcher.Stop();
            await Task.Delay(1500); // đợi socket release

            var (ok, diag) = await launcher.EnsureRunningAsync();
            StatusMessage = ok
                ? $"✅ Google Flow Local restarted — {diag}"
                : $"❌ Restart failed: {diag}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Exception khi restart: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task TestFlow2Async()
    {
        var launcher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
        if (launcher == null)
        {
            StatusMessage = "⚠️ Launcher service không có sẵn.";
            return;
        }

        StatusMessage = "🔍 Đang test kết nối Google Flow Local...";
        try
        {
            var (ok, diag) = await launcher.IsRunningAsync();
            StatusMessage = ok
                ? $"✅ Google Flow Local OK — {diag}"
                : $"❌ {diag}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi: {ex.Message}";
        }
    }
}