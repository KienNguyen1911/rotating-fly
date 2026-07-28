using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AssetAutomator.Services;
using AssetAutomator.Helpers;
using AssetAutomator.Models;
using AssetAutomator.Windows;

namespace AssetAutomator;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private IHost? _host;

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
            _host = CreateHostBuilder().Build();
            Services = _host.Services;

            // Load application settings on startup
            var configService = Services.GetRequiredService<IConfigService>();
            configService.LoadSettings();

            // Bridge DI config into the static YoutubeHelper so AutomationTask.OutputDir resolves correctly
            YoutubeHelper.ConfigServiceInstance = configService;

            // Start auto-update from GitHub
            var updateService = Services.GetRequiredService<UpdateService>();
            updateService.CheckForUpdates(isManualCheck: false);

            // Show main window
            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogCrash("OnStartupException", ex);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.AddSingleton<IConfigService, ConfigService>();

                // Core Services
                services.AddSingleton<IBrowserService, BrowserService>();
                services.AddSingleton<HistoryService>();
                services.AddSingleton<ImagePoolService>();
                services.AddSingleton<ChatGptService>();
                services.AddSingleton<UpdateService>();
                services.AddSingleton<LicenseService>();
                services.AddSingleton<GeminiApiService>();
                services.AddSingleton<GeminiCookieSyncService>();
                services.AddSingleton<BatchProjectService>();
                services.AddSingleton<BatchImageGenService>();
                services.AddSingleton<LegacyVideoPipelineService>();
                services.AddSingleton<GeminiVideoPipelineService>();
                services.AddSingleton<PipelineOrchestrator>();
                services.AddSingleton<PythonServerManager>(sp => PythonServerManager.Default);


                // Step Services
                services.AddSingleton<ThumbnailDownloadStep>();
                services.AddSingleton<TranscriptExtractionStep>();
                services.AddSingleton<ChatGptRewriteStep>();
                services.AddSingleton<VoiceoverGenerationStep>();
                services.AddSingleton<ImageGenerationStep>();
                services.AddSingleton<YoutubeTopicSuggestionStep>();
                services.AddSingleton<GeminiTopicResearchStep>();
                // Active: Playwright-only Scene Creator (drives real Gemini Web UI).
                services.AddSingleton<GeminiPlaywrightSceneBreakdownStep>();
                // Legacy: API-mode fallback, opt-in only. Registered explicitly for backward compatibility.
#pragma warning disable CS0618 // Type or member is obsolete
                services.AddSingleton<GeminiSceneBreakdownStepLegacy>();
#pragma warning restore CS0618
                services.AddSingleton<SceneImageBatchStep>();

                // ViewModels
                services.AddTransient<MainViewModel>();

                // Windows
                services.AddTransient<MainWindow>();
                services.AddTransient<LicenseWindow>();
                services.AddTransient<NewProjectWindow>();
                services.AddTransient<BulkTaskWindow>();
                services.AddTransient<VoiceSelectorWindow>();
                services.AddTransient<WebViewLoginWindow>();
                services.AddTransient<UpdateWindow>();
                services.AddTransient<ScenesViewerWindow>();
                services.AddTransient<ProxyTestResultWindow>();
            });
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
