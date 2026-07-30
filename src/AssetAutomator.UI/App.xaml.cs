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

            // Bridge static ConfigService.Instance to the injected IConfigService.
            // Required because legacy UI partial-classes still resolve ConfigService.Instance
            // statically (e.g. field initializers in MainWindow.BatchImageGen.cs).
            ConfigService.SetProvider(Services.GetRequiredService<Core.Interfaces.IConfigService>());

            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
