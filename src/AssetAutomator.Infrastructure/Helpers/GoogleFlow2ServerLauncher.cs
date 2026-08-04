using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using LogCategory = AssetAutomator.Core.LogCategory;

namespace AssetAutomator.Infrastructure.Helpers
{
    /// <summary>
    /// Owns the lifecycle of the Google Flow Local Python launcher shipped
    /// with the WinUI app under <c>tools/PythonSource/</c>. The launcher
    /// exposes an OpenAI-compatible image generation server on
    /// <c>http://127.0.0.1:8787/v1</c> that <c>FlowLocalImageGenProvider</c>
    /// talks to.
    ///
    /// Mirrors the behaviour of <c>start-flow-api.bat</c> shipped with the
    /// launcher repo:
    ///   1. Probe the configured port (8787) to see if the server is already up.
    ///   2. If not, prefer the bundled <c>tools/PythonEmbed/python.exe</c>
    ///      (installed by <c>Setup-PythonEmbed.ps1</c> at build time).
    ///      Fall back to <c>{rootPath}\.venv\Scripts\python.exe</c> for dev
    ///      setups or system <c>python</c> as last resort.
    ///   3. Spawn <c>python -m google_flow_ext.api.app --host 127.0.0.1 --port 8787</c>
    ///      with <c>FLOW_API_KEY=flow-local-key</c> in the environment.
    ///   4. Poll the port until the server responds.
    /// </summary>
    public class GoogleFlow2ServerLauncher
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

        private readonly ILogService _log;
        private readonly IConfigService _configService;
        private Process? _serverProcess;

        public GoogleFlow2ServerLauncher(ILogService logService, IConfigService configService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Health-check the configured Google Flow Local endpoint.
        /// Returns <c>true</c> if the launcher is responsive.
        ///
        /// Flow Local API (D:\Dev\google-flow-2.0.0\google_flow\api\app.py) exposes:
        ///   GET  /             → 200 (HTML login page)
        ///   GET  /health       → 200 {"status":"ok"}              ← canonical liveness
        ///   GET  /v1/models    → 401 (requires Bearer token)
        ///   POST /setup/open-login → triggers browser login flow
        ///   GET  /v1/health    → 404 (DOES NOT EXIST in this version)
        ///   GET  /openapi.json → 500 (intentional in this build)
        ///
        /// The probe order below starts at /health and walks outward. The 401
        /// response on /v1/models still proves the server is bound.
        /// </summary>
        public async Task<(bool IsRunning, string Diagnostics)> IsRunningAsync()
        {
            var settings = _configService.CurrentSettings;
            int port = settings.GoogleFlow2Port > 0 ? settings.GoogleFlow2Port : 8787;
            string baseUrl = $"http://127.0.0.1:{port}";

            // The launcher exposes /health (200), /v1/models (401/200), / (200).
            string[] probePaths = { "/health", "/v1/models", "/", "/setup", "/docs" };
            foreach (var path in probePaths)
            {
                try
                {
                    string url = baseUrl + path;
                    var response = await _httpClient.GetAsync(url);
                    int status = (int)response.StatusCode;
                    // 2xx = reachable; 401/403 = reachable (auth-gated), still alive
                    if (status < 500)
                    {
                        return (true, $"Flow Local probe OK — {url} responded {status}");
                    }
                }
                catch
                {
                    // try next probe
                }
            }

            return (false, $"No response from Flow Local launcher at {baseUrl} (tried /health, /v1/models, /, /setup, /docs).");
        }

        /// <summary>
        /// Ensures the Google Flow Local launcher is running. If the port is
        /// already serving, returns immediately. Otherwise spawns
        /// <c>python -m google_flow_ext.api.app --host 127.0.0.1 --port {port}</c>
        /// from <see cref="AppSettings.GoogleFlow2RootPath"/> (which defaults to
        /// the bundled <c>tools/PythonSource/</c>) and waits for the server to
        /// come up.
        /// </summary>
        public async Task<(bool Success, string Diagnostics)> EnsureRunningAsync()
        {
            var settings = _configService.CurrentSettings;

            if (!settings.GoogleFlow2AutoLaunch)
            {
                _log.Info(LogCategory.PythonServer, "Flow Local auto-launch disabled by config.");
                return (true, "Auto-launch disabled.");
            }

            string rootPath = settings.GoogleFlow2RootPath;
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                string message = $"Google Flow Local root not found: '{rootPath}'. Chạy tools/Scripts/Setup-PythonEmbed.ps1 để tạo tools/PythonSource/, hoặc sửa AppSettings.GoogleFlow2RootPath.";
                _log.Error(LogCategory.PythonServer, message);
                return (false, message);
            }

            var (running, runningDiag) = await IsRunningAsync();
            if (running)
            {
                _log.Debug(LogCategory.PythonServer, $"Flow Local already running: {runningDiag}");
                return (true, runningDiag);
            }

            int port = settings.GoogleFlow2Port > 0 ? settings.GoogleFlow2Port : 8787;
            string host = "127.0.0.1";

            string pythonExe = ResolvePythonExecutable(rootPath);
            if (!File.Exists(pythonExe) && pythonExe != "python")
            {
                _log.Warning(LogCategory.PythonServer, $"Python executable not found at '{pythonExe}', falling back to system 'python'.");
                pythonExe = "python";
            }

            // B1+B6: Self-heal thiếu dependency đã bị loại bỏ vì Setup-PythonEmbed.ps1 build-time
            // cài sẵn toàn bộ deps vào tools/PythonEmbed/site-packages. Nếu user chạy system
            // 'python' (không bundled), server vẫn fail → đó là expected behavior, không tự cài.

        try
        {
            // Use the EXTENDED server (google_flow_ext) because it returns
            // media_id + project_id in responses, has SHA256-cached reference
            // media via ExtendedFlowClient.upload_media_ext, and accepts
            // reference_media_id in /v1/images/generations to skip re-upload.
            // The original google_flow.api.app server does not return media_id
            // in its responses, so the WinUI-side _uploadedReferenceMediaIds
            // cache always missed and every batch item re-uploaded the same
            // reference image. See google_flow_ext/api/routes/openai_ext.py.
            // IMPORTANT: We use `python -c "import uvicorn; ..."` instead of
            // `python -m google_flow_ext.api.app`. Reason: Python embedded
            // (python311._pth) restricts sys.path and silently ignores argv[0]'s
            // directory for `-m`, so `python -m google_flow_ext.api.app` exits
            // immediately with no output when launched outside an interactive
            // shell. The explicit `python -c` form is reliable.
            //
            // The package is importable because `tools/PythonEmbed/Lib/site-packages/_assetautomator.pth`
            // (created by Setup-PythonEmbed.ps1) adds `../../../PythonSource`
            // to sys.path at interpreter startup.
            string appModule = "google_flow_ext.api.app:app";
            string uvicornArgs = "-c \"import uvicorn; uvicorn.run('{0}', host='{1}', port={2}, log_level='info')\""
                .Replace("{0}", appModule)
                .Replace("{1}", host)
                .Replace("{2}", port.ToString());

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = uvicornArgs,
                WorkingDirectory = rootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

                // Match start-flow-api.bat env. Flow Local API reads these.
                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                startInfo.EnvironmentVariables["FLOW_API_KEY"] = settings.ImageApiKey ?? "flow-local-key";
                startInfo.EnvironmentVariables["HOST"] = host;
                startInfo.EnvironmentVariables["API_PORT"] = port.ToString();

                // For system python (non-embedded), append the project root
                // because it doesn't load our .pth file.
                if (pythonExe == "python")
                {
                    string existingPath = startInfo.EnvironmentVariables["PYTHONPATH"] ?? string.Empty;
                    startInfo.EnvironmentVariables["PYTHONPATH"] = rootPath + ";" + existingPath;
                }

                _serverProcess = new Process { StartInfo = startInfo };
                _serverProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _log.Debug(LogCategory.PythonServer, $"[flow2] {e.Data}", "stdout");
                };
                _serverProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        bool isError = e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                       e.Data.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
                                       e.Data.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                                       e.Data.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
                                       e.Data.Contains("FATAL", StringComparison.OrdinalIgnoreCase);
                        if (isError)
                            _log.Error(LogCategory.PythonServer, $"[flow2] {e.Data}", "stderr");
                        else
                            _log.Debug(LogCategory.PythonServer, $"[flow2] {e.Data}", "stderr");
                    }
                };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();
                _log.Info(LogCategory.PythonServer, $"google-flow-2.0.0 launcher started (PID: {_serverProcess.Id}, host={host}:{port}, cwd={rootPath})");

                // Poll the port for ~15s (FastAPI/uvicorn cold start).
                int[] pollDelays = { 250, 250, 250, 500, 500, 500, 500, 500, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000 };
                foreach (int delayMs in pollDelays)
                {
                    await Task.Delay(delayMs);
                    if (_serverProcess.HasExited)
                    {
                        string exited = $"google-flow-2.0.0 launcher exited prematurely with code {_serverProcess.ExitCode}. Hãy kiểm tra .venv (chạy install.bat) hoặc quyền python.";
                        _log.Error(LogCategory.PythonServer, exited);
                        return (false, exited);
                    }

                    var (isUp, _) = await IsRunningAsync();
                    if (isUp)
                    {
                        _log.Success(LogCategory.PythonServer, $"google-flow-2.0.0 server is up (PID: {_serverProcess.Id}).");
                        return (true, "Server ready");
                    }
                }

                string timeout = "google-flow-2.0.0 server did not respond within ~15s.";
                _log.Error(LogCategory.PythonServer, timeout);
                return (false, timeout);
            }
            catch (Exception ex)
            {
                string message = $"Exception launching google-flow-2.0.0: {ex.GetType().Name}: {ex.Message}";
                _log.Error(LogCategory.PythonServer, message);
                return (false, message);
            }
        }

        public void Stop()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _log.Info(LogCategory.PythonServer, $"Stopping google-flow-2.0.0 launcher (PID: {_serverProcess.Id})...");
                    try
                    {
                        _serverProcess.Kill(entireProcessTree: true);
                        _serverProcess.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(LogCategory.PythonServer, $"Failed to kill launcher process: {ex.Message}");
                    }
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.PythonServer, $"Error stopping Flow Local launcher: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper: chạy một Python command và capture stdout/stderr/exit-code với timeout.
        /// Dùng cho self-heal + probe. Không dùng cho long-running server (=> dùng Process trực tiếp).
        /// </summary>
        private async Task<(int ExitCode, string Stdout, string Stderr)> RunPythonAsync(
            string pythonExe, string args, CancellationToken ct, int timeoutMs = 10_000)
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return (-1, string.Empty, "Process.Start returned null");
            }
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                return (-1, string.Empty, $"Process timed out after {timeoutMs}ms");
            }
            return (proc.ExitCode, await stdoutTask, await stderrTask);
        }

        /// <summary>
        /// Resolves the Python executable to invoke. Preference order:
        ///   1. <c>tools\PythonEmbed\python.exe</c> next to the WinUI exe
        ///      (bundled Python 3.11 embedded, deps installed at build time
        ///      by <c>tools/Scripts/Setup-PythonEmbed.ps1</c>).
        ///   2. <c>{rootPath}\.venv\Scripts\python.exe</c> (legacy dev setup
        ///      when running against an external repo clone).
        ///   3. System <c>python</c> on PATH (with PYTHONPATH including rootPath).
        /// </summary>
        public static string ResolvePythonExecutable(string rootPath)
        {
            string baseDir = AppContext.BaseDirectory;
            string embeddedPath = Path.Combine(baseDir, "tools", "PythonEmbed", "python.exe");
            if (File.Exists(embeddedPath)) return embeddedPath;

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                string venvPy = Path.Combine(rootPath, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPy)) return venvPy;

                string venvPyLinux = Path.Combine(rootPath, ".venv", "bin", "python");
                if (File.Exists(venvPyLinux)) return venvPyLinux;
            }

            return "python";
        }
    }
}