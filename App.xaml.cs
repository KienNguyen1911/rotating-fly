using System;
using System.IO;
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
        
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash("DispatcherUnhandledException", args.Exception);
        };

        try
        {
            // Load application settings on startup
            ConfigService.LoadSettings();

            // Bắt đầu cập nhật tự động từ GitHub
            var updateService = new UpdateService();
            updateService.CheckForUpdates(isManualCheck: false);
        }
        catch (Exception ex)
        {
            LogCrash("OnStartupException", ex);
        }
    }

    private static void LogCrash(string context, Exception? ex)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] {ex?.ToString()}\n";
            File.AppendAllText(logPath, msg);
        }
        catch { }
    }
}
