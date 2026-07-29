using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WpfApplication = System.Windows.Application;

namespace AssetAutomator.UI
{
    public partial class App : WpfApplication
    {
        public static IServiceProvider Services { get; private set; } = null!;
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [UnhandledException] {args.ExceptionObject}\n");
                }
                catch { }
            };

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<Core.Interfaces.IConfigService, Infrastructure.Services.ConfigService>();
                    services.AddSingleton<Core.Interfaces.ILogService, Infrastructure.Logging.LogService>();
                    services.AddSingleton<Application.Services.HistoryService>();
                    services.AddSingleton<Application.Services.LicenseService>();
                    services.AddSingleton<Application.Services.ChatGptService>();
                })
                .Build();

            Services = _host.Services;
            MainWindow = new Windows.MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
