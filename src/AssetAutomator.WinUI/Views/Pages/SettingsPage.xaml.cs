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

        // Populate PasswordBox controls on page load
        PbApiKey.Password = ViewModel.ApiKey ?? string.Empty;
        PbAi84ApiKey.Password = ViewModel.Ai84ApiKey ?? string.Empty;
        PbSupabaseDbUrl.Password = ViewModel.SupabaseDbUrl ?? string.Empty;
        PbImageApiKey.Password = ViewModel.ImageApiKey ?? string.Empty;

        // Wire PasswordChanged events to sync back to ViewModel
        PbApiKey.PasswordChanged += (s, e) => ViewModel.ApiKey = PbApiKey.Password;
        PbAi84ApiKey.PasswordChanged += (s, e) => ViewModel.Ai84ApiKey = PbAi84ApiKey.Password;
        PbSupabaseDbUrl.PasswordChanged += (s, e) => ViewModel.SupabaseDbUrl = PbSupabaseDbUrl.Password;
        PbImageApiKey.PasswordChanged += (s, e) => ViewModel.ImageApiKey = PbImageApiKey.Password;

        // Refresh PasswordBox controls when settings are imported
        ViewModel.SettingsImported += (s, e) =>
        {
            PbApiKey.Password = ViewModel.ApiKey ?? string.Empty;
            PbAi84ApiKey.Password = ViewModel.Ai84ApiKey ?? string.Empty;
            PbSupabaseDbUrl.Password = ViewModel.SupabaseDbUrl ?? string.Empty;
            PbImageApiKey.Password = ViewModel.ImageApiKey ?? string.Empty;
        };

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