using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Steps;

namespace AssetAutomator.WinUI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private IHost? _host;
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();

        // Defense-in-depth: prevent stray async exceptions from crashing the process.
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[UnobservedTaskException] {e.Exception?.Message}");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {ex?.Message}");
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Core Infrastructure Services
                services.AddSingleton<IConfigService, Infrastructure.Services.ConfigService>();
                services.AddSingleton<ILogService, Infrastructure.Logging.LogService>();

                // Application Services with Logging Callbacks
                services.AddSingleton<HistoryService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogService>();
                    return new HistoryService(msg => logger.Info(LogCategory.General, msg));
                });

                services.AddSingleton<BrowserService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogService>();
                    return new BrowserService(msg => logger.Info(LogCategory.Pipeline, msg));
                });
                services.AddSingleton<IBrowserService>(sp => sp.GetRequiredService<BrowserService>());

                services.AddSingleton<LicenseService>();
                services.AddSingleton<ChatGptService>();
                services.AddSingleton<ImagePoolService>();
                services.AddSingleton<BatchProjectService>();
                services.AddSingleton<GeminiApiService>();
                services.AddSingleton<BatchImageGenService>();
                services.AddSingleton<Infrastructure.Helpers.PythonServerManager>();
                services.AddSingleton<YoutubeTopicSuggestionStep>();

                services.AddSingleton<GeminiCreatorService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogService>();
                    var config = sp.GetRequiredService<IConfigService>();
                    var geminiApi = sp.GetRequiredService<GeminiApiService>();
                    var pythonServer = sp.GetRequiredService<Infrastructure.Helpers.PythonServerManager>();
                    return new GeminiCreatorService(logger, config, geminiApi, pythonServer);
                });

                // Pipeline Steps
                services.AddSingleton<VoiceoverGenerationStep>();
                services.AddSingleton<GeminiPlaywrightSceneBreakdownStep>();
                services.AddSingleton<GeminiTopicResearchStep>();

                // Orchestrator
                services.AddSingleton<PipelineOrchestrator>();

                // ViewModels
                services.AddSingleton<ViewModels.SidebarViewModel>();

                // Most WinUI ViewModels have nullable default params on their
                // constructors (legacy pattern). MS.DI silently injects null for
                // those params even when the service IS registered (see
                // Microsoft.Extensions.DependencyInjection ActivatorUtilities
                // behavior + Stack Overflow #60379123). Explicit factories below
                // force each dependency to be resolved via GetRequiredService.
                services.AddTransient<ViewModels.TasksViewModel>(sp => new ViewModels.TasksViewModel(
                    sp.GetRequiredService<PipelineOrchestrator>(),
                    sp.GetRequiredService<HistoryService>(),
                    sp.GetRequiredService<ILogService>(),
                    sp.GetRequiredService<IConfigService>()
                ));
                services.AddTransient<ViewModels.PoolViewModel>(sp => new ViewModels.PoolViewModel(
                    sp.GetRequiredService<ImagePoolService>(),
                    sp.GetRequiredService<IConfigService>()
                ));
                services.AddTransient<ViewModels.ProfilesViewModel>(sp => new ViewModels.ProfilesViewModel(
                    sp.GetRequiredService<BrowserService>(),
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<ILogService>()
                ));
                services.AddTransient<ViewModels.HistoryViewModel>(sp => new ViewModels.HistoryViewModel(
                    sp.GetRequiredService<HistoryService>()
                ));
                services.AddTransient<ViewModels.SettingsViewModel>(sp => new ViewModels.SettingsViewModel(
                    sp.GetRequiredService<IConfigService>()
                ));
                services.AddTransient<ViewModels.BatchImageGenViewModel>(sp => new ViewModels.BatchImageGenViewModel(
                    sp.GetRequiredService<BatchProjectService>(),
                    sp.GetRequiredService<BatchImageGenService>(),
                    sp.GetRequiredService<IConfigService>()
                ));

                // GeminiViewModel needs explicit factory wiring — its constructor
                // accepts nullable params (legacy pattern) which makes MS.DI inject
                // null into every dependency. The factory below resolves each
                // dependency via GetRequiredService so the VM actually gets the
                // real services instead of silent nulls (see StackOverflow #60379123).
                services.AddTransient<ViewModels.GeminiViewModel>(sp => new ViewModels.GeminiViewModel(
                    sp.GetRequiredService<GeminiCreatorService>(),
                    sp.GetRequiredService<PipelineOrchestrator>(),
                    sp.GetRequiredService<YoutubeTopicSuggestionStep>(),
                    sp.GetRequiredService<GeminiApiService>(),
                    sp.GetRequiredService<Infrastructure.Helpers.PythonServerManager>(),
                    sp.GetRequiredService<IConfigService>(),
                    sp.GetRequiredService<ILogService>(),
                    sp.GetRequiredService<IBrowserService>()
                ));

                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();
        Services = _host.Services;

        _window = Services.GetRequiredService<MainWindow>();
        MainWindowInstance = _window;
        _window.Activate();
    }

    public async Task ShutdownAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
    }
}
