using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly BrowserService? _browserService;
    private readonly IConfigService? _configService;
    private readonly ILogService? _logService;

    [ObservableProperty]
    private ObservableCollection<string> _profiles = new();

    [ObservableProperty]
    private string? _selectedProfile;

    [ObservableProperty]
    private string _proxyAddress = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Chrome Profile Manager sẵn sàng";

    [ObservableProperty]
    private bool _isBusy;

    // Input fields for new profile creation + custom GPT URL
    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _customGptUrl = string.Empty;

    public ProfilesViewModel(BrowserService? browserService = null, IConfigService? configService = null, ILogService? logService = null)
    {
        _browserService = browserService;
        _configService = configService;
        _logService = logService;
        LoadProfiles();
        LoadCustomGptUrl();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        string baseDir = GetProfilesDirectory();

        if (Directory.Exists(baseDir))
        {
            var dirs = Directory.GetDirectories(baseDir);
            foreach (var dir in dirs)
            {
                Profiles.Add(Path.GetFileName(dir));
            }
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add("Default_Profile");
            Profiles.Add("Automation_Profile_1");
            Profiles.Add("Automation_Profile_2");
        }

        SelectedProfile = Profiles[0];
        StatusMessage = $"Đã nạp {Profiles.Count} Chrome Profiles từ {baseDir}";
    }

    private void LoadCustomGptUrl()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            CustomGptUrl = settings.CustomGptUrl ?? string.Empty;
        }
    }

    private string GetProfilesDirectory()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            if (!string.IsNullOrWhiteSpace(settings.ChromeProfilesDir))
            {
                return settings.ChromeProfilesDir;
            }
        }
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user-data-dir");
    }

    [RelayCommand]
    private async Task LaunchProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
        {
            StatusMessage = "Vui lòng chọn một Chrome Profile.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Đang khởi chạy Chrome với Profile '{SelectedProfile}'...";

        try
        {
            string baseDir = GetProfilesDirectory();
            string profilePath = Path.Combine(baseDir, SelectedProfile);
            Directory.CreateDirectory(profilePath);

            // Find chrome executable
            string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
            if (!File.Exists(chromePath))
            {
                chromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
            }

            if (File.Exists(chromePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = chromePath,
                    Arguments = $"--user-data-dir=\"{profilePath}\" --no-first-run --no-default-browser-check",
                    UseShellExecute = true
                };
                Process.Start(psi);
                StatusMessage = $"Đã mở Chrome Profile '{SelectedProfile}' thành công.";
            }
            else
            {
                StatusMessage = "Không tìm thấy trình duyệt Google Chrome được cài đặt tại C:\\Program Files.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở Chrome Profile: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenProfileFolder()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
        {
            StatusMessage = "Vui lòng chọn một Chrome Profile.";
            return;
        }
        try
        {
            string baseDir = GetProfilesDirectory();
            string profilePath = Path.Combine(baseDir, SelectedProfile);
            Directory.CreateDirectory(profilePath);

            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{profilePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            StatusMessage = $"Đã mở thư mục Profile '{SelectedProfile}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi mở thư mục: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SetDefaultProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
        {
            StatusMessage = "Vui lòng chọn một Chrome Profile.";
            return;
        }
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            settings.DefaultChromeProfile = SelectedProfile;
            _configService.SaveSettings(settings);
        }
        StatusMessage = $"Đã đặt '{SelectedProfile}' làm Chrome Profile mặc định.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfile))
        {
            StatusMessage = "Vui lòng chọn một Chrome Profile để xóa.";
            return;
        }

        try
        {
            string baseDir = GetProfilesDirectory();
            string profilePath = Path.Combine(baseDir, SelectedProfile);
            if (Directory.Exists(profilePath))
            {
                Directory.Delete(profilePath, recursive: true);
            }
            Profiles.Remove(SelectedProfile);
            SelectedProfile = Profiles.Count > 0 ? Profiles[0] : null;
            StatusMessage = $"Đã xóa Chrome Profile '{SelectedProfile}'.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi xóa Profile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CreateProfile()
    {
        string baseDir = GetProfilesDirectory();
        Directory.CreateDirectory(baseDir);

        // Use user-provided name if set; otherwise auto-generate.
        string newName = string.IsNullOrWhiteSpace(NewProfileName)
            ? $"Profile_{Profiles.Count + 1}"
            : NewProfileName.Trim();

        string newPath = Path.Combine(baseDir, newName);
        if (Directory.Exists(newPath))
        {
            StatusMessage = $"Profile '{newName}' đã tồn tại tại {newPath}.";
            return;
        }

        Directory.CreateDirectory(newPath);
        Profiles.Add(newName);
        SelectedProfile = newName;
        NewProfileName = string.Empty;
        StatusMessage = $"Đã tạo mới Chrome Profile '{newName}' tại {newPath}.";
    }

    [RelayCommand]
    private void SaveCustomGptUrl()
    {
        if (_configService != null)
        {
            var settings = _configService.LoadSettings();
            settings.CustomGptUrl = CustomGptUrl ?? string.Empty;
            _configService.SaveSettings(settings);
        }
        StatusMessage = "Đã lưu URL Custom GPT.";
    }

    [RelayCommand]
    private async Task TestProxyAsync()
    {
        if (string.IsNullOrWhiteSpace(ProxyAddress))
        {
            StatusMessage = "Vui lòng nhập địa chỉ Proxy (vd: http://192.168.1.1:8080).";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Đang kiểm tra kết nối Proxy: {ProxyAddress}...";

        try
        {
            var handler = new HttpClientHandler
            {
                Proxy = new System.Net.WebProxy(ProxyAddress),
                UseProxy = true
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            var response = await client.GetStringAsync("https://api.ipify.org");
            StatusMessage = $"Kết nối Proxy THÀNH CÔNG! IP Xuất bản: {response.Trim()}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi kết nối Proxy: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}