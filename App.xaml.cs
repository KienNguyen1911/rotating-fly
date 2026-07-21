using System.Configuration;
using System.Data;
using System.Windows;

namespace AssetAutomator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Bắt đầu cập nhật tự động từ GitHub
        AutoUpdaterDotNET.AutoUpdater.Start("https://raw.githubusercontent.com/KienNguyen1911/rotating-fly/main/update.xml");
        
    }
}
