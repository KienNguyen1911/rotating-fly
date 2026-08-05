using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Steps;
using AssetAutomator.Infrastructure.Http;

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

        // Safety net: when the WinUI process is about to exit (graceful close,
        // crash, or taskkill), make sure the Google Flow Local Python launcher
        // we spawned gets killed. Without this, the orphan python.exe keeps
        // port 8787 bound and the next launch fails with "address already in use".
        // ProcessExit is a best-effort sync hook — Windows gives us ~1-2s.
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            try
            {
                var launcher = Services?.GetService<Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
                launcher?.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessExit] {ex.Message}");
            }
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

                // ─────────────────────────────────────────────────────
                //  Named HttpClient + Polly resilience pipelines
                //  All AI84 HTTP call sites MUST go through these so
                //  they get a sane timeout (default 100s is way too long)
                //  plus retry / circuit-breaker protection.
                // ─────────────────────────────────────────────────────
                // Standard client: lookup, submit, download. 60s per-attempt timeout,
                // 3 retries with exponential backoff + jitter, 50% circuit breaker
                // over a 30s window after 5 calls.
                services
                    .AddHttpClient(VoiceoverGenerationStep.Ai84HttpClientName, client =>
                    {
                        // Outer ceiling — slightly larger than the inner per-attempt timeout
                        // so a successful retry that takes 60s is still allowed to complete
                        // before the HttpClient itself throws TimeoutException.
                        client.Timeout = TimeSpan.FromSeconds(180);
                    })
                    .AddResilienceHandler("ai84-std", builder =>
                    {
                        ResiliencePipelineDefaults.ConfigureStandardPipeline(builder);
                    });

                // Long-polling client: AI84 job-status polling. No circuit breaker
                // (server is responding, just slowly), 120s per-attempt timeout,
                // 5 retries.
                services
                    .AddHttpClient(VoiceoverGenerationStep.Ai84PollingHttpClientName, client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(300); // outer ceiling for 5 × 120s attempts
                    })
                    .AddResilienceHandler("ai84-polling", builder =>
                    {
                        ResiliencePipelineDefaults.ConfigureLongPollingPipeline(builder);
                    });

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
                services.AddSingleton<Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
                services.AddSingleton<YoutubeTopicSuggestionStep>();

                services.AddSingleton<GeminiCreatorService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogService>();
                    var config = sp.GetRequiredService<IConfigService>();
                    var geminiApi = sp.GetRequiredService<GeminiApiService>();
                    var pythonServer = sp.GetRequiredService<Infrastructure.Helpers.PythonServerManager>();
                    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                    return new GeminiCreatorService(logger, config, geminiApi, pythonServer, httpClientFactory);
                });

                // Pipeline Steps
                services.AddSingleton<VoiceoverGenerationStep>();
                services.AddSingleton<GeminiPlaywrightSceneBreakdownStep>();
                services.AddSingleton<GeminiTopicResearchStep>();
                services.AddSingleton<SceneImageBatchStep>();

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

                // GeminiViewModel is registered as Singleton so its state (GeminiTasks,
                // SelectedTask, AvailableScriptwriterGems, ConsoleLogs, ...) survives
                // page navigation. Without this, every Navigate(typeof(GeminiPage))
                // would re-construct the VM and the user's tasks would disappear.
                services.AddSingleton<ViewModels.GeminiViewModel>(sp => new ViewModels.GeminiViewModel(
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

        // Surface WinUI 3 UI-thread exceptions (XAML framework, binding fails, etc.)
        // that would otherwise silently terminate the process. WinUI 3 Application
        // class exposes UnhandledException; setting Handled=true prevents the
        // native runtime from tearing down the process.
        this.UnhandledException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[UI UnhandledException] {e.Message}");
            e.Handled = true;
        };

        // Fire-and-forget: ensure google-flow-2.0.0 launcher is up so the
        // Google Flow Local image-gen API is available without manual setup.
        // Failures are surfaced via UI dialog and ILogService, not silently.
        _ = Task.Run(async () =>
        {
            try
            {
                // Bảo đảm Python embedded + bundled source đã sẵn sàng (1 lần đầu).
                // Setup script tải Python 3.11 embed, cài deps (playwright, nodriver, Pillow...),
                // mirror google_flow/ + google_flow_ext/ vào tools/PythonSource/.
                // Đánh dấu hoàn thành bằng `.installed-marker`. Khi marker tồn tại → skip.
                // Check marker (không chỉ python.exe) vì python.exe có thể có sẵn nhưng
                // dependencies (playwright, Pillow, nodriver, ...) lại thiếu.
                string embeddedPython = Path.Combine(AppContext.BaseDirectory, "tools", "PythonEmbed", "python.exe");
                string embeddedMarker = Path.Combine(AppContext.BaseDirectory, "tools", "PythonEmbed", ".installed-marker");
                string setupScript = Path.Combine(AppContext.BaseDirectory, "tools", "Scripts", "Setup-PythonEmbed.ps1");
                if ((!File.Exists(embeddedPython) || !File.Exists(embeddedMarker)) && File.Exists(setupScript))
                {
                    System.Diagnostics.Debug.WriteLine("[Flow Local] Python embedded not fully set up → running Setup-PythonEmbed.ps1 ...");
                    await RunSetupScriptAsync(setupScript);
                }

                var launcher = Services.GetService<Infrastructure.Helpers.GoogleFlow2ServerLauncher>();
                if (launcher == null)
                {
                    System.Diagnostics.Debug.WriteLine("[Flow Local] launcher service not registered.");
                    return;
                }
                var (ok, diag) = await launcher.EnsureRunningAsync();
                if (!ok)
                {
                    System.Diagnostics.Debug.WriteLine($"[Flow Local] auto-launch failed: {diag}");
                    // Dispatch to UI thread → ContentDialog
                    if (_window?.DispatcherQueue is { } dq)
                    {
                        var tcs = new TaskCompletionSource();
                        dq.TryEnqueue(async () =>
                        {
                            try { await ShowFlowLocalStartupFailureDialogAsync(diag); }
                            finally { tcs.SetResult(); }
                        });
                        await tcs.Task;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Flow Local] auto-launch threw: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Chạy Setup-PythonEmbed.ps1 ở background để download Python Embedded + cài deps + mirror
    /// google_flow + google_flow_ext. PowerShell Core (pwsh) ưu tiên, fallback Windows PowerShell 5.1.
    /// </summary>
    private async Task RunSetupScriptAsync(string setupScriptPath)
    {
        string pwsh = TryFindPwsh() ?? "powershell";
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pwsh,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{setupScriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(startInfo);
            if (proc == null)
            {
                System.Diagnostics.Debug.WriteLine($"[Setup] Không spawn được '{pwsh}' — bỏ qua auto-setup.");
                return;
            }

            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    System.Diagnostics.Debug.WriteLine($"[setup] {e.Data}");
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    System.Diagnostics.Debug.WriteLine($"[setup:err] {e.Data}");
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Setup] Setup-PythonEmbed.ps1 exited with code {proc.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Setup] Failed to launch setup script: {ex.Message}");
        }
    }

    private static string? TryFindPwsh()
    {
        // Where.exe lookup (PowerShell Core SDK installs here).
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "pwsh.exe"),
            "pwsh"
        };
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p)) return p;
            }
            catch { /* keep searching */ }
        }
        return null;
    }

    /// <summary>
    /// B2: ContentDialog cảnh báo khi launch Flow Local thất bại.
    /// Cho phép user nhảy thẳng vào SettingsPage để sửa path/config.
    /// </summary>
    private async Task ShowFlowLocalStartupFailureDialogAsync(string diagnostics)
    {
        try
        {
            if (_window?.Content is not Microsoft.UI.Xaml.FrameworkElement root) return;
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "⚠️ Google Flow Local không khởi động được",
                Content = $"Không bật được Google Flow Local server.\n\n"
                          + $"Chi tiết: {diagnostics}\n\n"
                          + "Bạn có thể tạo ảnh bằng provider khác (G-Labs) hoặc cấu hình lại đường dẫn repo trong Settings.",
                CloseButtonText = "Đóng",
                PrimaryButtonText = "Mở Settings",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                XamlRoot = root.XamlRoot
            };
            dialog.PrimaryButtonClick += (_, _) =>
            {
                if (_window?.Content is Microsoft.UI.Xaml.Controls.Grid grid)
                {
                    var navView = FindVisualChild<Microsoft.UI.Xaml.Controls.NavigationView>(grid);
                    if (navView != null)
                    {
                        navView.SelectedItem = navView.FooterMenuItems
                            .OfType<Microsoft.UI.Xaml.Controls.NavigationViewItem>()
                            .FirstOrDefault(i => (i.Tag as string) == "settings");
                    }
                }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowFlowLocalStartupFailureDialogAsync] {ex.Message}");
        }
    }

    private static T? FindVisualChild<T>(Microsoft.UI.Xaml.DependencyObject parent) where T : Microsoft.UI.Xaml.DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deeper = FindVisualChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
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
