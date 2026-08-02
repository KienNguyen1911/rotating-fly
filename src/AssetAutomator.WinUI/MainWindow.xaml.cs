using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;
using AssetAutomator.WinUI.Views.Pages;

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

    private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var theme = ThemeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        RootGrid.RequestedTheme = theme;
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