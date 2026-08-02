using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Windows.Graphics;
using WinRT.Interop;
using AssetAutomator.WinUI.ViewModels;
using AssetAutomator.WinUI.Views.Pages;
using AssetAutomator.WinUI.Views.Dialogs;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Application.Services;

namespace AssetAutomator.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SetSize();
        ExtendIntoTitleBar();

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(TasksPage));
    }

    private void SetSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1240, 700));
    }

    private void ExtendIntoTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    /// <summary>
    /// Public entry point used by SettingsPage to switch the global theme.
    /// Replaces the old ToggleSwitch in the header.
    /// </summary>
    public void SetTheme(ElementTheme theme)
    {
        RootGrid.RequestedTheme = theme;
    }

    /// <summary>Opens the License dialog. Invoked from SettingsPage.</summary>
    public async void OpenLicenseDialog()
    {
        try
        {
            BtnLicense_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenLicenseDialog] {ex.Message}");
        }
    }

    /// <summary>Triggers an update check. Invoked from SettingsPage.</summary>
    public async void OpenUpdateCheck()
    {
        try
        {
            BtnCheckUpdate_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenUpdateCheck] {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────
    //  D1 — License & Update header buttons
    // ─────────────────────────────────────────────────────

    private async void BtnLicense_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var licenseService = App.Services.GetService<LicenseService>();
            var configService = App.Services.GetService<IConfigService>();
            var dialog = new LicenseDialog(licenseService, configService)
            {
                XamlRoot = RootGrid.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BtnLicense_Click] {ex.Message}");
        }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Quick check: fetch the latest GitHub release. If the version differs from
            // the running app's assembly version, show the UpdateDialog.
            string installedVersion = GetInstalledVersion();
            string? latestVersion = await FetchLatestVersionAsync();

            if (latestVersion != null && !string.Equals(latestVersion, installedVersion, StringComparison.OrdinalIgnoreCase))
            {
                var dialog = new UpdateDialog(installedVersion: installedVersion, latestVersion: latestVersion)
                {
                    XamlRoot = RootGrid.XamlRoot
                };
                await dialog.ShowAsync();
            }
            else
            {
                var info = new ContentDialog
                {
                    Title = "✅ Đã cập nhật",
                    Content = $"Bạn đang dùng phiên bản mới nhất (v{installedVersion}).",
                    CloseButtonText = "Đóng",
                    XamlRoot = RootGrid.XamlRoot
                };
                await info.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BtnCheckUpdate_Click] {ex.Message}");
        }
    }

    private static string GetInstalledVersion()
    {
        // Unpackaged WinUI 3 app: Package.Current is not available, fall back to assembly metadata.
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var asmVersion = asm.GetName().Version;
        return asmVersion != null
            ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}"
            : "1.0.0";
    }

    private static async Task<string?> FetchLatestVersionAsync()
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AssetAutomator-App");
            const string apiUrl = "https://api.github.com/repos/KienNguyen1911/AssetAutomator-Releases/releases/latest";
            var response = await http.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tag))
            {
                string raw = tag.GetString() ?? string.Empty;
                return raw.TrimStart('v', 'V');
            }
        }
        catch
        {
            // network failures, etc.
        }
        return null;
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag as string)
            {
                case "tasks":
                    ContentFrame.Navigate(typeof(TasksPage));
                    break;
                case "pool":
                    ContentFrame.Navigate(typeof(PoolPage));
                    break;
                case "profiles":
                    ContentFrame.Navigate(typeof(ProfilesPage));
                    break;
                case "gemini":
                    ContentFrame.Navigate(typeof(GeminiPage));
                    break;
                case "batch":
                    ContentFrame.Navigate(typeof(BatchImageGenPage));
                    break;
                case "history":
                    ContentFrame.Navigate(typeof(HistoryPage));
                    break;
                case "settings":
                    ContentFrame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
    }
}