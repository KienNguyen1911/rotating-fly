using System;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
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
    private DispatcherTimer? _flowLocalStatusTimer;

    public MainWindow()
    {
        InitializeComponent();
        SetSize();
        ExtendIntoTitleBar();

        // Wire Resources/app_logo.ico into the WinUI 3 window icon (title bar + taskbar).
        // Unpackaged WinUI 3 ignores <ApplicationIcon> at runtime, so the .exe icon shown by
        // Explorer/taskbar must be set explicitly via AppWindow.SetIcon + SetTaskbarIcon.
        TryApplyAppLogo();

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(GeminiPage));

        this.Closed += MainWindow_Closed;
        this.Activated += MainWindow_Activated;

        // Q3: Poll Flow Local status mỗi 3s để update BottomBar indicator
        StartFlowLocalStatusPolling();
    }

    private void TryApplyAppLogo()
    {
        try
        {
            var icoPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Resources", "app_logo.ico");
            if (!System.IO.File.Exists(icoPath))
            {
                System.Diagnostics.Debug.WriteLine($"[Icon] app_logo.ico not found at '{icoPath}'");
                return;
            }

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            // WinUI 3 AppWindow exposes (string filePath) overloads for both icons.
            // We let the framework own the HICON lifecycle — no manual DestroyIcon needed.
            appWindow.SetIcon(icoPath);
            appWindow.SetTaskbarIcon(icoPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Icon] Failed to apply app logo: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-applies the taskbar + title-bar icon. WinUI 3 has a well-known quirk where the
    /// taskbar icon is cached by Explorer the first time the HWND is shown and is not
    /// refreshed even after AppWindow.SetTaskbarIcon is called later (e.g. when the icon
    /// resource was missing during the very first Activate). Hooking Activated ensures
    /// the icon sticks as soon as the window has a real HWND on screen.
    /// </summary>
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Trigger một lần update ngay khi window activated
        _ = UpdateFlowLocalIndicatorAsync();

        // Cheap idempotent call; harmless if already applied.
        TryApplyAppLogo();
    }

    private void StartFlowLocalStatusPolling()
    {
        _flowLocalStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _flowLocalStatusTimer.Tick += async (_, _) => await UpdateFlowLocalIndicatorAsync();
        _flowLocalStatusTimer.Start();
    }

    private async Task UpdateFlowLocalIndicatorAsync()
    {
        try
        {
            var launcher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
            if (launcher == null)
            {
                FlowLocalIndicator.Text = "Flow Local: chưa đăng ký";
                FlowLocalIcon.Glyph = "\uE783"; // warning
                return;
            }
            var (ok, diag) = await launcher.IsRunningAsync();
            if (ok)
            {
                FlowLocalIndicator.Text = "Flow Local: ✅ sẵn sàng";
                FlowLocalIcon.Glyph = "\uE73E"; // check
                FlowLocalIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
            }
            else
            {
                FlowLocalIndicator.Text = "Flow Local: 🔄 đang khởi động...";
                FlowLocalIcon.Glyph = "\uE9CE"; // sync
                FlowLocalIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
            }
        }
        catch
        {
            // best-effort; indicator sẽ retry sau 3s
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            _flowLocalStatusTimer?.Stop();

            var pythonServerManager = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.PythonServerManager>();
            pythonServerManager?.StopServer();

            // B3: Stop Google Flow Local server khi app đóng, tránh orphan process
            // giữ port 8787 → lần sau launch fail "address already in use".
            var flowLauncher = App.Services.GetService<AssetAutomator.Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
            flowLauncher?.Stop();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow_Closed] {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────
    //  BottomBar Flow Local indicator — click handlers
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Mở dashboard Google Flow Local (http://127.0.0.1:8787) trong trình duyệt mặc định
    /// khi user click vào bottom bar indicator. Dùng Windows.System.Launcher để hệ thống
    /// tự chọn handler phù hợp (Edge/Chrome/...). Nếu server chưa chạy, browser vẫn mở
    /// và hiển thị lỗi kết nối — không nuốt lỗi để user biết trạng thái thực tế.
    /// </summary>
    private async void FlowLocalIndicator_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        try
        {
            const string FlowLocalUrl = "http://127.0.0.1:8787";
            var uri = new Uri(FlowLocalUrl);
            bool launched = await Windows.System.Launcher.LaunchUriAsync(uri);
            if (!launched)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[FlowLocalIndicator_Tapped] Launcher.LaunchUriAsync returned false for '{FlowLocalUrl}'.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FlowLocalIndicator_Tapped] {ex.Message}");
        }
    }

    private void FlowLocalIndicator_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Hover feedback: tăng Opacity + đổi BorderBrush để người dùng biết đây là phần tử clickable.
        // Đổi cursor sang bàn tay qua reflection vì UIElement.ProtectedCursor là protected setter,
        // code-behind ngoài partial class không gọi trực tiếp được.
        if (sender is Microsoft.UI.Xaml.Controls.Border border)
        {
            border.Opacity = 1.0;
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["AccentFillColorDefaultBrush"];
        }
        TrySetHandCursor(sender as UIElement);
    }

    private void FlowLocalIndicator_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Border border)
        {
            border.Opacity = 0.92;
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"];
        }
        TrySetArrowCursor(sender as UIElement);
    }

    // UIElement.ProtectedCursor là protected setter. Code-behind của MainWindow không kế thừa UIElement
    // nên không gọi trực tiếp được — dùng reflection để set một lần, cache PropertyInfo để tránh lookup lặp.
    private static System.Reflection.PropertyInfo? s_protectedCursorProp;
    private static System.Reflection.PropertyInfo GetProtectedCursorProperty()
    {
        if (s_protectedCursorProp == null)
        {
            s_protectedCursorProp = typeof(UIElement).GetProperty(
                "ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        }
        return s_protectedCursorProp!;
    }

    private static void TrySetHandCursor(UIElement? element)
    {
        if (element == null) return;
        try
        {
            var cursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
            GetProtectedCursorProperty().SetValue(element, cursor);
        }
        catch
        {
            // best-effort; nếu SDK đổi API, hover chỉ mất cursor chứ không crash app.
        }
    }

    private static void TrySetArrowCursor(UIElement? element)
    {
        if (element == null) return;
        try
        {
            var cursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            GetProtectedCursorProperty().SetValue(element, cursor);
        }
        catch
        {
            // best-effort
        }
    }

    private void SetSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
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

    /// <summary>
    /// Returns the currently active theme from RootGrid.
    /// </summary>
    public ElementTheme GetCurrentTheme()
    {
        return RootGrid.RequestedTheme;
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