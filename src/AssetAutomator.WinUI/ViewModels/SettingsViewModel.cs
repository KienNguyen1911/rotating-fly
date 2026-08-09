using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AssetAutomator.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigService? _configService;
    private readonly IWatermarkRemover? _watermarkRemover;
    private readonly ILogService? _log;
    public event EventHandler? SettingsImported;

    // Watermark test workflow: temp file gets cleaned up when user picks a
    // different image, runs again, or the page unloads. We never overwrite
    // the user's original file.
    private string? _testTempFilePath;
    private CancellationTokenSource? _testRunCts;

    [ObservableProperty]
    private string _outputPath = @"C:\AssetAutomator\Outputs";

    [ObservableProperty]
    private int _maxConcurrentThreads = 3;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private string _statusMessage = "Cài đặt hệ thống sẵn sàng.";

    // Additional API Keys & URLs
    [ObservableProperty]
    private string _ai84ApiKey = string.Empty;

    [ObservableProperty]
    private string _imageApiUrl = string.Empty;

    [ObservableProperty]
    private string _imageApiKey = string.Empty;

    [ObservableProperty]
    private string _subtitleApiUrl = string.Empty;

    // B4: Google Flow Local server fields
    [ObservableProperty]
    private string _googleFlow2RootPath = Path.Combine(
        AppContext.BaseDirectory, "tools", "PythonSource");

    [ObservableProperty]
    private int _googleFlow2Port = 8787;

    [ObservableProperty]
    private bool _googleFlow2AutoLaunch = true;

    // Directories
    [ObservableProperty]
    private string _chromeProfilesDir = string.Empty;

    [ObservableProperty]
    private string _projectsStorageDir = string.Empty;

    // ─────────────────────────────────────────────────────
    //  Gemini Watermark Removal — wiltodelta/remove-ai-watermarks (Python).
    //  - EnableWatermarkRemoval  : master toggle.
    //  - WatermarkMaxParallel    : subprocess concurrency.
    //  - WatermarkInpaintBackend : auto / cv2 / migan / lama.
    //  - WatermarkPerImageTimeoutSec : per-image ceiling.
    //
    //  All other knobs (catalog position, catalog size, region override,
    //  python path) were removed because wiltodelta's `visible` subcommand
    //  auto-detects position+size from the image itself; passing hints
    //  had zero effect on the actual CLI invocation.
    // ─────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _enableWatermarkRemoval = true;

    [ObservableProperty]
    private int _watermarkMaxParallel = 0;

    // wiltodelta/remove-ai-watermarks (Python) knobs.
    // Chỉ 2 field có ý nghĩa runtime — backend (auto/cv2/migan/lama) và
    // timeout per-image. Mọi vị trí/kích thước catalog đều bị CLI bỏ qua
    // (auto-detect từ ảnh) nên đã được dọn khỏi UI.
    [ObservableProperty]
    private string _watermarkInpaintBackend = "auto";

    [ObservableProperty]
    private int _watermarkPerImageTimeoutSec = 30;

    // ─────────────────────────────────────────────────────
    //  Watermark removal TEST workflow (independent of the batch pipeline).
    //  Lets the user pick a single image via File Explorer, run gwr CLI on
    //  a temp copy (so the original is never overwritten), and view the
    //  after-result inline. Lives in SettingsViewModel because the test only
    //  makes sense in the Settings page context, not BatchImageGen.
    // ─────────────────────────────────────────────────────
    [ObservableProperty]
    private string _testImagePath = string.Empty;

    [ObservableProperty]
    private string _watermarkTestStatus = "Chưa chọn ảnh.";

    [ObservableProperty]
    private bool _isRunningWatermarkTest;

    [ObservableProperty]
    private BitmapImage? _testResultImage;

    public bool CanRunWatermarkTest =>
        !string.IsNullOrWhiteSpace(TestImagePath)
        && File.Exists(TestImagePath)
        && !IsRunningWatermarkTest;

    public SettingsViewModel(
        IConfigService? configService = null,
        IWatermarkRemover? watermarkRemover = null,
        ILogService? log = null)
    {
        _configService = configService;
        _watermarkRemover = watermarkRemover;
        _log = log;
        LoadSettings();
    }

    /// <summary>
    /// Called from <see cref="SettingsPage.OnUnloaded"/> to cancel any
    /// in-flight Run + delete the temp file copy. Keeps the user's disk clean.
    /// </summary>
    public void CleanupTempFile()
    {
        try { _testRunCts?.Cancel(); } catch { /* swallow */ }
        _testRunCts?.Dispose();
        _testRunCts = null;
        DeleteTempFile();
    }

    private void DeleteTempFile()
    {
        if (string.IsNullOrEmpty(_testTempFilePath)) return;
        try
        {
            if (File.Exists(_testTempFilePath))
            {
                File.Delete(_testTempFilePath);
            }
        }
        catch (Exception ex)
        {
            _log?.Warning(LogCategory.General, $"Failed to delete temp file: {ex.Message}");
        }
        finally
        {
            _testTempFilePath = null;
        }
    }

    partial void OnTestImagePathChanged(string value)
    {
        // When the user picks a different image, clear the previous result
        // and the temp file. The bound Button.IsEnabled reacts to
        // CanRunWatermarkTest re-evaluation automatically.
        TestResultImage = null;
        DeleteTempFile();
        OnPropertyChanged(nameof(CanRunWatermarkTest));
    }

    partial void OnIsRunningWatermarkTestChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunWatermarkTest));
    }

    private void LoadSettings()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            Ai84ApiKey = settings.Ai84ApiKey ?? string.Empty;
            ImageApiUrl = settings.ImageApiUrl ?? string.Empty;
            ImageApiKey = settings.ImageApiKey ?? string.Empty;
            ChromeProfilesDir = settings.ChromeProfilesDir ?? string.Empty;
            SubtitleApiUrl = settings.SubtitleApiUrl ?? string.Empty;
            OutputPath = settings.OutputsDir ?? @"C:\AssetAutomator\Outputs";
            ProjectsStorageDir = settings.ProjectsStorageDir ?? string.Empty;
            MaxConcurrentThreads = settings.MaxConcurrentTasks > 0 ? settings.MaxConcurrentTasks : 3;
            GoogleFlow2RootPath = settings.GoogleFlow2RootPath ?? Path.Combine(
                AppContext.BaseDirectory, "tools", "PythonSource");
            GoogleFlow2Port = settings.GoogleFlow2Port > 0 ? settings.GoogleFlow2Port : 8787;
            GoogleFlow2AutoLaunch = settings.GoogleFlow2AutoLaunch;

            // Watermark removal
            EnableWatermarkRemoval = settings.EnableWatermarkRemoval;
            WatermarkMaxParallel = settings.WatermarkMaxParallel;
            WatermarkInpaintBackend = string.IsNullOrWhiteSpace(settings.WatermarkInpaintBackend)
                ? "auto" : settings.WatermarkInpaintBackend;
            WatermarkPerImageTimeoutSec = settings.WatermarkPerImageTimeoutSec > 0
                ? settings.WatermarkPerImageTimeoutSec : 30;
        }
    }

    private static int Clamp(int v, int min, int max, int fallback)
    {
        if (v < min || v > max) return fallback;
        return v;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            settings.Ai84ApiKey = Ai84ApiKey;
            settings.OutputsDir = OutputPath;
            settings.MaxConcurrentTasks = MaxConcurrentThreads;
            settings.ImageApiUrl = ImageApiUrl;
            settings.ImageApiKey = ImageApiKey;
            settings.ChromeProfilesDir = ChromeProfilesDir;
            settings.SubtitleApiUrl = SubtitleApiUrl;
            settings.ProjectsStorageDir = ProjectsStorageDir;
            settings.GoogleFlow2RootPath = GoogleFlow2RootPath;
            settings.GoogleFlow2Port = GoogleFlow2Port;
            settings.GoogleFlow2AutoLaunch = GoogleFlow2AutoLaunch;

            // Watermark removal
            settings.EnableWatermarkRemoval = EnableWatermarkRemoval;
            settings.WatermarkMaxParallel = WatermarkMaxParallel;
            settings.WatermarkInpaintBackend = WatermarkInpaintBackend;
            settings.WatermarkPerImageTimeoutSec = WatermarkPerImageTimeoutSec;

            _configService.SaveSettings(settings);
        }

        StatusMessage = "Đã lưu cấu hình thành công!";
    }

    [RelayCommand]
    private void BrowseFolder()
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
                OutputPath = folder.Path;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở Folder Picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseProjectsStorageDir()
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
                ProjectsStorageDir = folder.Path;
                StatusMessage = $"Đã chọn thư mục Projects Batch Image Gen: {folder.Path}";
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
            launcher.Stop();
            await Task.Delay(1500);

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

    /// <summary>
    /// Probes the wiltodelta Python watermark-remover stack end-to-end:
    ///   1. Verifies the embedded Python (or system Python) is reachable.
    ///   2. Verifies the <c>remove-ai-watermarks[visible]</c> package is
    ///      importable (one-time pip install if missing).
    ///   3. Reports the resolved Python path + package version.
    /// Bound to the "Kiểm tra Python & CLI" button in the Watermark card.
    /// </summary>
    [RelayCommand]
    private async Task CheckWatermarkCliAsync()
    {
        var launcher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.PythonLauncher>();
        if (launcher == null)
        {
            StatusMessage = "⚠️ PythonLauncher chưa được đăng ký trong DI.";
            return;
        }

        StatusMessage = "🔍 Đang kiểm tra Python + remove-ai-watermarks (cold-start có thể mất ~30s cho pip)...";
        try
        {
            var (ok, diag) = await launcher.EnsureInstalledAsync();
            StatusMessage = ok
                ? $"✅ Watermark CLI sẵn sàng — {diag}"
                : $"❌ Watermark CLI chưa sẵn sàng — {diag}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Lỗi kiểm tra Watermark CLI: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens a File Explorer picker so the user can choose a single PNG/JPEG/WebP
    /// to test watermark removal on. Stores the path on <see cref="TestImagePath"/>.
    /// </summary>
    [RelayCommand]
    private async Task PickTestImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");

            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                TestImagePath = file.Path;
                WatermarkTestStatus = $"Đã chọn: {Path.GetFileName(file.Path)} ({file.FileType})";
            }
        }
        catch (Exception ex)
        {
            WatermarkTestStatus = $"Lỗi mở File Picker: {ex.Message}";
        }
    }

    /// <summary>
    /// Runs the gwr CLI on a TEMP COPY of the user's chosen image so the
    /// original file is never overwritten. The CLI is passed a 30s per-image
    /// timeout — most cleanups finish in &lt;1s. Result is loaded into
    /// <see cref="TestResultImage"/>; the temp file is deleted on next pick
    /// or page unload.
    /// </summary>
    [RelayCommand]
    private async Task RunWatermarkTestAsync()
    {
        if (_watermarkRemover == null)
        {
            WatermarkTestStatus = "⚠️ IWatermarkRemover chưa được đăng ký trong DI.";
            return;
        }
        if (!File.Exists(TestImagePath))
        {
            WatermarkTestStatus = "⚠️ File không tồn tại.";
            return;
        }

        // Defensive: clear pending run + previous temp file
        try { _testRunCts?.Cancel(); } catch { /* swallow */ }
        _testRunCts?.Dispose();
        _testRunCts = new CancellationTokenSource();
        DeleteTempFile();

        IsRunningWatermarkTest = true;
        TestResultImage = null;
        WatermarkTestStatus = "Đang chuẩn bị temp copy...";

        try
        {
            // Copy original to temp so the user's file stays untouched.
            string ext = Path.GetExtension(TestImagePath);
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                $"watermark_test_{Guid.NewGuid():N}{ext}");
            File.Copy(TestImagePath, tempPath, overwrite: true);
            _testTempFilePath = tempPath;

            WatermarkTestStatus = $"Đang chạy gwr CLI trên temp copy... (size: {new FileInfo(tempPath).Length / 1024} KB)";
            _log?.Info(LogCategory.General, $"Watermark test: running gwr on {tempPath}");

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                _testRunCts.Token,
                new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);

            var result = await _watermarkRemover.RemoveAsync(tempPath, linked.Token);

            if (!result.Applied)
            {
                WatermarkTestStatus = $"❌ CLI không áp dụng được: {result.Reason}";
                _log?.Warning(LogCategory.General, $"Watermark test not applied: {result.Reason}");
                DeleteTempFile();
                return;
            }

            // CLI overwrote tempPath in-place. Load the result as a BitmapImage
            // for the AFTER viewer (use a fresh stream so the file lock is released).
            WatermarkTestStatus = $"✅ Xong trong {result.DurationMs / 1000.0:F1}s — đang tải kết quả...";
            _log?.Success(LogCategory.General, $"Watermark test OK in {result.DurationMs}ms");

            // Try a few times because the CLI may still hold the file briefly
            // after returning. ~150ms total spin is enough in practice.
            BitmapImage? bmp = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    bmp = new BitmapImage();
                    using var stream = File.OpenRead(tempPath);
                    await bmp.SetSourceAsync(stream.AsRandomAccessStream());
                    break;
                }
                catch (IOException) when (i < 4)
                {
                    await Task.Delay(50);
                }
            }

            if (bmp == null)
            {
                WatermarkTestStatus = "❌ Không đọc được file output sau khi CLI chạy xong.";
            }
            else
            {
                TestResultImage = bmp;
                WatermarkTestStatus = $"✅ Hoàn tất ({result.DurationMs / 1000.0:F1}s) — ảnh gốc KHÔNG bị thay đổi.";
            }
        }
        catch (OperationCanceledException)
        {
            WatermarkTestStatus = "⏹️ Đã hủy.";
            DeleteTempFile();
        }
        catch (Exception ex)
        {
            WatermarkTestStatus = $"❌ Lỗi: {ex.GetType().Name}: {ex.Message}";
            _log?.Error(LogCategory.General, $"Watermark test failed: {ex.Message}");
            DeleteTempFile();
        }
        finally
        {
            IsRunningWatermarkTest = false;
        }
    }
}
