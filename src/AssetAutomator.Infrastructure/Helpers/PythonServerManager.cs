using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Infrastructure.Helpers
{
    public class PythonServerManager
    {
        public static PythonServerManager Default { get; set; } = null!;
        private readonly ILogService _log;
        private readonly IConfigService _configService;
        private Process? _serverProcess;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };

        public PythonServerManager(ILogService logService, IConfigService configService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            Default = this;
        }

        public async Task<(bool IsRunning, string Diagnostics)> IsServerRunningAsync(string baseUrl = "http://localhost:8000")
        {
            try
            {
                string healthUrl = baseUrl.TrimEnd('/') + "/api/health";
                var response = await _httpClient.GetAsync(healthUrl);
                return response.IsSuccessStatusCode
                    ? (true, $"Health check OK — {baseUrl}/api/health responded {response.StatusCode}")
                    : (false, $"Health check failed — {baseUrl}/api/health returned {response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Cannot connect to {baseUrl}: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, $"Connection to {baseUrl} timed out after 3s");
            }
            catch (Exception ex)
            {
                return (false, $"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            }
        }

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

            string pythonExe = ResolvePythonExecutable();
            string serverScriptPath = ResolveServerScriptPath();
            if (!File.Exists(serverScriptPath))
            {
                string message = $"server.py not found at: {serverScriptPath}";
                _log.Error(LogCategory.PythonServer, message);
                return (false, message);
            }

            if (!File.Exists(pythonExe) && pythonExe != "python")
            {
                _log.Warning(LogCategory.PythonServer, $"Embedded Python not found at '{pythonExe}', falling back to system 'python'");
                pythonExe = "python";
            }

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
                startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                string scriptDir = Path.GetDirectoryName(serverScriptPath)!;
                startInfo.EnvironmentVariables["PYTHONPATH"] = Path.Combine(scriptDir, "src") + ";" + (Environment.GetEnvironmentVariable("PYTHONPATH") ?? "");

                var settings = _configService.CurrentSettings;
                if (!string.IsNullOrEmpty(settings.ChromeProfilesDir))
                    startInfo.EnvironmentVariables["CHROME_PROFILES_DIR"] = settings.ChromeProfilesDir;
                if (!string.IsNullOrEmpty(settings.DefaultChromeProfile))
                    startInfo.EnvironmentVariables["DEFAULT_CHROME_PROFILE"] = settings.DefaultChromeProfile;

                _serverProcess = new Process { StartInfo = startInfo };
                _serverProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        _log.Debug(LogCategory.PythonServer, e.Data, "stdout");
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
                            _log.Error(LogCategory.PythonServer, e.Data, "stderr");
                        else
                            _log.Debug(LogCategory.PythonServer, e.Data, "stderr");
                    }
                };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();
                _log.Info(LogCategory.PythonServer, $"Python server process started (PID: {_serverProcess.Id})");

                int[] pollDelays = { 250, 250, 250, 250, 250, 250, 250, 250, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500, 500 };
                foreach (int delayMs in pollDelays)
                {
                    await Task.Delay(delayMs);
                    if (_serverProcess.HasExited)
                    {
                        string message = $"Python server process exited prematurely with code {_serverProcess.ExitCode}";
                        _log.Error(LogCategory.PythonServer, message);
                        return (false, message);
                    }

                    var (isRunning, _) = await IsServerRunningAsync(baseUrl);
                    if (isRunning)
                    {
                        _log.Success(LogCategory.PythonServer, $"Server successfully launched (PID: {_serverProcess.Id})");
                        return (true, "Server ready");
                    }
                }

                string timeoutMessage = $"Server did not respond within 8s at {baseUrl}/api/health";
                _log.Error(LogCategory.PythonServer, timeoutMessage);
                return (false, timeoutMessage);
            }
            catch (Exception ex)
            {
                string message = $"Exception launching Python server: {ex.GetType().Name}: {ex.Message}";
                _log.Error(LogCategory.PythonServer, message);
                return (false, message);
            }
        }

        public string ResolvePythonExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string embeddedPath = Path.Combine(baseDir, "tools", "PythonEmbed", "python.exe");
            if (File.Exists(embeddedPath))
                return embeddedPath;

            string relativePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tools", "PythonEmbed", "python.exe"));
            return File.Exists(relativePath) ? relativePath : "python";
        }

        public string ResolveServerScriptPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "server.py");
            return File.Exists(scriptPath)
                ? scriptPath
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Modules", "Gemini-API-2.0.0", "server.py"));
        }

        public async Task<(bool Success, string Diagnostics)> RestartServerAsync(string baseUrl = "http://localhost:8000")
        {
            _log.Info(LogCategory.PythonServer, "Restarting Gemini Python Server...");
            StopServer();
            await Task.Delay(300);
            var result = await EnsureServerRunningAsync(baseUrl, skipInitialCheck: true);
            if (result.Success)
                _log.Success(LogCategory.PythonServer, "Server restarted successfully.");
            else
                _log.Error(LogCategory.PythonServer, $"Server restart failed: {result.Diagnostics}");
            return result;
        }

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
                    _log.Success(LogCategory.PythonServer, "Server process killed.");
                }
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.PythonServer, $"Error stopping server: {ex.Message}");
            }
        }

        public (bool IsRunning, int? Pid, string? StartTime) GetProcessInfo()
        {
            if (_serverProcess == null)
                return (false, null, null);
            try
            {
                return (!_serverProcess.HasExited, _serverProcess.Id, _serverProcess.StartTime.ToString("HH:mm:ss"));
            }
            catch
            {
                return (false, null, null);
            }
        }
    }
}
