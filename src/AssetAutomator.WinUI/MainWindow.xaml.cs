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
    public SidebarViewModel Sidebar { get; }

    private bool _isDraggingSidebar;
    private double _dragStartX;
    private double _dragStartWidth;

    public MainWindow()
    {
        Sidebar = App.Services.GetRequiredService<SidebarViewModel>();
        InitializeComponent();
        SetSize();
        ExtendIntoTitleBar();

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(TasksPage));

        Sidebar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.IsOpen))
            {
                UpdateSidebarVisibility();
            }
        };
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

    // ─────────────────────────────────────────────────────
    //  D2 — Sidebar drawer (drag-handle, slide animation)
    // ─────────────────────────────────────────────────────

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        Sidebar.Toggle();
    }

    private void BtnCloseSidebar_Click(object sender, RoutedEventArgs e)
    {
        Sidebar.Close();
    }

    private void UpdateSidebarVisibility()
    {
        if (Sidebar.IsOpen)
        {
            // Slide in: bring visibility on, then animate X from +DrawerWidth to 0.
            SidebarDrawerOverlay.Visibility = Visibility.Visible;
            SidebarTranslate.X = Sidebar.DrawerWidth;
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = Sidebar.DrawerWidth,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, SidebarTranslate);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        else
        {
            // Slide out: animate X from 0 to +DrawerWidth, then hide.
            var storyboard = new Storyboard();
            double currentDrawerWidth = Sidebar.DrawerWidth;
            var animation = new DoubleAnimation
            {
                From = 0,
                To = currentDrawerWidth,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(animation, SidebarTranslate);
            Storyboard.SetTargetProperty(animation, "X");
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) => SidebarDrawerOverlay.Visibility = Visibility.Collapsed;
            storyboard.Begin();
        }
    }

    private void SidebarDragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pp = e.GetCurrentPoint(SidebarDrawer);
        if (!pp.Properties.IsLeftButtonPressed) return;
        _isDraggingSidebar = true;
        _dragStartX = e.GetCurrentPoint(RootGrid).Position.X;
        _dragStartWidth = Sidebar.DrawerWidth;
        SidebarDragHandle.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void SidebarDragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSidebar) return;

        double currentX = e.GetCurrentPoint(RootGrid).Position.X;
        double delta = _dragStartX - currentX; // dragging left → width grows
        double newWidth = _dragStartWidth + delta;

        double maxWidth = RootGrid.ActualWidth * SidebarViewModel.MaxWidthRatio;
        if (maxWidth < SidebarViewModel.MinWidth) maxWidth = SidebarViewModel.MinWidth;
        newWidth = Math.Max(SidebarViewModel.MinWidth, Math.Min(newWidth, maxWidth));

        Sidebar.DrawerWidth = newWidth;
    }

    private void SidebarDragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSidebar) return;
        _isDraggingSidebar = false;
        SidebarDragHandle.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void SidebarDragHandle_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // The ResizableDragHandle subclass sets the resize cursor via ProtectedCursor.
        // Nothing to do here; the handler exists so the XAML can hook PointerEntered.
    }

    private void SidebarDragHandle_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Reset cursor when the pointer leaves the handle (and we're not currently dragging).
        if (!_isDraggingSidebar && sender is Controls.ResizableDragHandle handle)
        {
            handle.ResetCursor();
        }
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