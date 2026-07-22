using System.Configuration;
using System.Data;
using System.Windows;
using AssetAutomator.Services;

namespace AssetAutomator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Load application settings on startup
        ConfigService.LoadSettings();

        // Bắt đầu cập nhật tự động từ GitHub
        var updateService = new UpdateService();
        updateService.CheckForUpdates(isManualCheck: false);
    }
}
