using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Services;
using AssetAutomator.Services.Logging;

namespace AssetAutomator.Helpers
{
    /// <summary>
    /// Manages launching and ensuring health of the embedded Python Gemini WebAPI Server (server.py).
    /// Uses ILogService for all diagnostics — logs are visible both in Trace/DebugView AND app UI.
    /// Prefers embedded Python at PythonEmbed/python.exe if available.
    /// </summary>
    public class PythonServerManager
    {
        private readonly ILogService _log;
        private Process? _serverProcess;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

        public PythonServerManager(ILogService logService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <summary>
        /// Checks if the Gemini API Server (http://localhost:8000/api/health) is running.
        /// Returns (isRunning, diagnosticsMessage) tuple for detailed error reporting.
        /// </summary>
        public async Task<(bool IsRunning, string Diagnostics)> IsServerRunningAsync(string baseUrl = "http://localhost:8000")
        {
            try
            {
                string healthUrl = baseUrl.TrimEnd('/') + "/api/health";
                var response = await _httpClient.GetAsync(healthUrl);
                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Health check OK — {baseUrl}/api/health responded {response.StatusCode}");
                }
                return (false, $"Health check failed — {baseUrl}/api/health returned {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot connect to {baseUrl}: {ex.Message} (server may not be running)");
            }
            catch (TaskCanceledException)
            {
                return (false, $"Connection to {baseUrl} timed out after 3s (server not running or port blocked)");
            }
            catch (Exception ex)
            {
                return (false, $"Unexpected error checking server: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the Python server is running. Launches server.py in background if needed.
        /// skipInitialCheck=true bypasses the first health check (use after killing the server).
        /// Returns (success, diagnostics) tuple for detailed reporting.
        /// </summary>
        public async Task<(bool Success, string Diagnostics)> EnsureServerRunningAsync(string baseUrl = "http://localhost:8000", bool skipInitialCheck = false)
        {
            if (!skipInitialCheck)
            {
                var (isRunning, diag) = await IsServerRunningAsync(baseUrl);
                if (isRunning)
                {
                    _log.Debug(LogCategory.PythonServer, $"Server already running: {diag}");
                    return (true, diag);
                }
            }

            // Find Python executable (prefer PythonEmbed/python.exe)
            string pythonExe = ResolvePythonExecutable();
            string serverScriptPath = ResolveServerScriptPath();

            if (!File.Exists(serverScriptPath))
            {
                string errMsg = $"server.py not found at: {serverScriptPath}";
                _log.Error(LogCategory.PythonServer, errMsg);
                return (false, errMsg);
            }

            if (!File.Exists(pythonExe) && pythonExe != "python")
            {
                string warnMsg = $"Embedded Python not found at '{pythonExe}', falling back to system 'python'";
                _log.Warning(LogCategory.PythonServer, warnMsg);
                pythonExe = "python";
            }

            _log.Info(LogCategory.PythonServer, $"Launching Python server...");
            _log.Debug(LogCategory.PythonServer, $"  Python:   {pythonExe}");
            _log.Debug(LogCategory.PythonServer, $"  Script:   {serverScriptPath}");
            _log.Debug(LogCategory.PythonServer, $"  WorkDir:  {Path.GetDirectoryName(serverScriptPath)}");

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{serverScriptPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(serverScriptPath),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1"; // Force unbuffered stdout for real-time logs
                string scriptDir = Path.GetDirectoryName(serverScriptPath)!;
                string srcDir = Path.Combine(scriptDir, "src");
                startInfo.EnvironmentVariables["PYTHONPATH"] = srcDir + ";" + (Environment.GetEnvironmentVariable("PYTHONPATH") ?? "");

                var settings = ConfigService.CurrentSettings;
                if (!string.IsNullOrEmpty(settings.ChromeProfilesDir))
                {
                    startInfo.EnvironmentVariables["CHROME_PROFILES_DIR"] = settings.ChromeProfilesDir;
                    _log.Debug(LogCategory.PythonServer, $"  CHROME_PROFILES_DIR={settings.ChromeProfilesDir}");
                }
                if (!string.IsNullOrEmpty(settings.DefaultChromeProfile))
                {
                    startInfo.EnvironmentVariables["DEFAULT_CHROME_PROFILE"] = settings.DefaultChromeProfile;
                }

                _serverProcess = new Process { StartInfo = startInfo };

                // ── Route stdout/stderr to ILogService so they appear in app UI ──
                _serverProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        _log.Debug(LogCategory.PythonServer, e.Data, "stdout");
                    }
                };

                _serverProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        // Python prints some normal messages to stderr (e.g. uvicorn startup)
                        // Only mark as Warning if it looks like an actual error
                        bool isLikelyError =
                            e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("CRITICAL", StringComparison.OrdinalIgnoreCase) ||
                            e.Data.Contains("FATAL", StringComparison.OrdinalIgnoreCase);

                        if (isLikelyError)
                            _log.Error(LogCategory.PythonServer, e.Data, "stderr");
                        else
                            _log.Debug(LogCategory.PythonServer, e.Data, "stderr");
                    }
                };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                _log.Info(LogCategory.PythonServer, $"Python server process started (PID: {_serverProcess.Id})");

                // Adaptive polling: kiểm tra nhanh lúc đầu (250ms), thưa dần về sau (500ms)
                // Server Flask/FastAPI thường khởi động trong 1-3 giây → max 8s là dư
                int[] pollDelays = { 250, 250, 250, 250, 250, 250, 250, 250,  // 8×250ms = 2s fast phase
                                     500, 500, 500, 500, 500, 500, 500, 500,  // 8×500ms = 4s
                                     500, 500, 500, 500 };                      // 4×500ms = 2s → tổng 8s

                foreach (int delayMs in pollDelays)
                {
                    await Task.Delay(delayMs);

                    // Check if process crashed early
                    if (_serverProcess.HasExited)
                    {
                        int exitCode = _serverProcess.ExitCode;
                        string crashMsg = $"Python server process exited prematurely with code {exitCode}. Check Python server logs above for details.";
                        _log.Error(LogCategory.PythonServer, crashMsg);
                        return (false, crashMsg);
                    }

                    var (isRunning, diag) = await IsServerRunningAsync(baseUrl);
                    if (isRunning)
                    {
                        _log.Success(LogCategory.PythonServer, $"Server successfully launched and responding on /api/health (PID: {_serverProcess.Id})");
                        return (true, "Server ready — /api/health OK");
                    }
                }

                string timeoutMsg = $"Server did not respond within 8s timeout at {baseUrl}/api/health. Check the Python server logs above for startup errors.";
                _log.Error(LogCategory.PythonServer, timeoutMsg);
                return (false, timeoutMsg);
            }
            catch (Exception ex)
            {
                string errMsg = $"Exception while launching Python server: {ex.GetType().Name}: {ex.Message}";
                _log.Error(LogCategory.PythonServer, errMsg);
                return (false, errMsg);
            }
        }

        /// <summary>
        /// Resolves path to Python executable, preferring embedded Python.
        /// </summary>
        public string ResolvePythonExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string embeddedPythonPath = Path.Combine(baseDir, "tools", "PythonEmbed", "python.exe");

            if (File.Exists(embeddedPythonPath))
            {
                return embeddedPythonPath;
            }

            // Check relative to working directory / solution root
            string relativeEmbeddedPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "PythonEmbed", "python.exe"));
            if (File.Exists(relativeEmbeddedPath))
            {
                return relativeEmbeddedPath;
            }

            // Fallback to system python
            return "python";
        }

        /// <summary>
        /// Resolves path to Modules/Gemini-API-2.0.0/server.py.
        /// </summary>
        public string ResolveServerScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "server.py");

            if (File.Exists(scriptPath))
            {
                return scriptPath;
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Modules", "Gemini-API-2.0.0", "server.py"));
        }

        /// <summary>
        /// Restarts the background Python Gemini WebAPI server process to ensure fresh cookie loading.
        /// Optimized: skips initial health check (server known dead), no artificial delays.
        /// Returns (success, diagnostics) tuple.
        /// </summary>
        public async Task<(bool Success, string Diagnostics)> RestartServerAsync(string baseUrl = "http://localhost:8000")
        {
            _log.Info(LogCategory.PythonServer, "Restarting Gemini Python Server...");
            StopServer();

            // Đợi một chút để OS giải phóng port, rồi khởi động lại ngay
            await Task.Delay(300);
            var result = await EnsureServerRunningAsync(baseUrl, skipInitialCheck: true);

            if (result.Success)
                _log.Success(LogCategory.PythonServer, "Server restarted successfully.");
            else
                _log.Error(LogCategory.PythonServer, $"Server restart failed: {result.Diagnostics}");

            return result;
        }

        /// <summary>
        /// Stops the background server process safely upon application shutdown or server restart.
        /// Kills the tracked process tree immediately.
        /// </summary>
        public void StopServer()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _log.Info(LogCategory.PythonServer, $"Killing server process (PID: {_serverProcess.Id})...");
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.WaitForExit(3000);
                    _serverProcess.Dispose();
                    _serverProcess = null;
                    _log.Success(LogCategory.PythonServer, "Server process killed successfully.");
                }
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.PythonServer, $"Error stopping server process: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the current server process info for diagnostics display.
        /// </summary>
        public (bool IsRunning, int? Pid, string? StartTime) GetProcessInfo()
        {
            if (_serverProcess == null)
                return (false, null, null);

            try
            {
                return (!_serverProcess.HasExited,
                        _serverProcess.Id,
                        _serverProcess.StartTime.ToString("HH:mm:ss"));
            }
            catch
            {
                return (false, null, null);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Legacy static API — delegates to a default instance
        //  (for backward compatibility with existing callers)
        // ─────────────────────────────────────────────────────

        private static PythonServerManager? _defaultInstance;
        private static readonly object _defaultLock = new();

        /// <summary>
        /// Gets or creates the default singleton instance backed by LogService.
        /// </summary>
        public static PythonServerManager Default
        {
            get
            {
                if (_defaultInstance == null)
                {
                    lock (_defaultLock)
                    {
                        _defaultInstance ??= new PythonServerManager(new Services.Logging.LogService());
                    }
                }
                return _defaultInstance;
            }
        }
    }
}
