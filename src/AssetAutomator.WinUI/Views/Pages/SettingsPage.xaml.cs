using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.WinUI.ViewModels;
using AssetAutomator.WinUI.Views.Dialogs;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;

        // Sync toggle with current theme at load time.
        if (App.MainWindowInstance is MainWindow mw)
        {
            ThemeToggle.IsOn = mw.Content is FrameworkElement fe && fe.RequestedTheme == ElementTheme.Dark;
        }
    }

    // Wire the buttons that used to live in the MainWindow header.
    private void BtnLicense_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is MainWindow mw) mw.OpenLicenseDialog();
    }

    private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is MainWindow mw) mw.OpenUpdateCheck();
    }

    private void ThemeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is MainWindow mw)
        {
            mw.SetTheme(ThemeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light);
        }
    }
}