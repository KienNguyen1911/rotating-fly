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
    private bool _isThemeToggleInitialized = false;

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;

        // Populate PasswordBox controls on page load
        PbAi84ApiKey.Password = ViewModel.Ai84ApiKey ?? string.Empty;
        PbImageApiKey.Password = ViewModel.ImageApiKey ?? string.Empty;

        // Wire PasswordChanged events to sync back to ViewModel
        PbAi84ApiKey.PasswordChanged += (s, e) => ViewModel.Ai84ApiKey = PbAi84ApiKey.Password;
        PbImageApiKey.PasswordChanged += (s, e) => ViewModel.ImageApiKey = PbImageApiKey.Password;

        // Refresh PasswordBox controls when settings are imported
        ViewModel.SettingsImported += (s, e) =>
        {
            PbAi84ApiKey.Password = ViewModel.Ai84ApiKey ?? string.Empty;
            PbImageApiKey.Password = ViewModel.ImageApiKey ?? string.Empty;
        };

        // Sync toggle with current theme at load time.
        if (App.MainWindowInstance is MainWindow mw)
        {
            _isThemeToggleInitialized = true;
            ThemeToggle.IsOn = mw.Content is FrameworkElement fe && fe.RequestedTheme == ElementTheme.Dark;
            _isThemeToggleInitialized = false;
        }

        // Cancel any in-flight Run + delete temp file when leaving the page.
        // Without this, leaving the page mid-run would leak the temp file in
        // %TEMP% and the CancellationTokenSource would never be disposed.
        Unloaded += SettingsPage_Unloaded;
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.CleanupTempFile();
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
        if (_isThemeToggleInitialized) return;

        if (App.MainWindowInstance is MainWindow mw)
        {
            var newTheme = ThemeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light;
            if (mw.GetCurrentTheme() != newTheme)
            {
                mw.SetTheme(newTheme);
            }
        }
    }
}