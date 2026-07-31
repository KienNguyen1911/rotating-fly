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
                    // Health check OK — but make sure the listener is *our* python process, not a stale orphan.
                    int ourPid = _serverProcess is { HasExited: false } alive ? alive.Id : -1;
                    int livePid = GetListeningPid(baseUrl);
                    if (ourPid > 0 && livePid > 0 && livePid != ourPid)
                    {
                        _log.Warning(LogCategory.PythonServer,
                            $"Health check OK but port 8000 is held by foreign PID {livePid} (ours={ourPid}). Killing stale server and re-spawning.");
                        try { KillProcessTree(livePid); }
                        catch (Exception ex) { _log.Warning(LogCategory.PythonServer, $"Failed to kill stale server {livePid}: {ex.Message}"); }
                        await Task.Delay(500);
                    }
                    else
                    {
                        _log.Debug(LogCategory.PythonServer, $"Server already running: {diag}");
                        return (true, diag);
                    }
                }
            }

            // Defensive: if any foreign process is already on port 8000 (cold-start orphan), kill it first
            // before spawning so our child can bind.
            int portHolder = GetListeningPid(baseUrl);
            if (portHolder > 0 && (_serverProcess is null || _serverProcess.HasExited || portHolder != _serverProcess.Id))
            {
                _log.Warning(LogCategory.PythonServer,
                    $"Port 8000 occupied by foreign PID {portHolder} — killing before spawn to free the socket.");
                try { KillProcessTree(portHolder); }
                catch (Exception ex) { _log.Warning(LogCategory.PythonServer, $"Failed to kill port holder {portHolder}: {ex.Message}"); }
                await Task.Delay(500);
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

            // Even when restarting "ourselves", a stale foreign process may still own the socket
            // from a previous orphan run. Re-route through EnsureServerRunningAsync which now
            // detects and clears port collisions before spawning.
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
                    KillProcessTree(_serverProcess.Id);
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

        // ─────────────────────────────────────────────────────
        //  Port-conflict recovery helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns the PID currently holding the listening TCP port for <paramref name="baseUrl"/>,
        /// or -1 if no listener found / port is free.
        /// Uses netstat (output parse) because Get-NetTCPConnection requires PowerShell+CIM modules
        /// which may not be present on minimal Windows installations.
        /// </summary>
        private int GetListeningPid(string baseUrl)
        {
            try
            {
                int port = 8000;
                try
                {
                    var uri = new Uri(baseUrl);
                    port = uri.Port;
                }
                catch { /* default 8000 */ }

                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano -p TCP",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return -1;
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(2000);

                foreach (var raw in output.Split('\n'))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                    var parts = System.Text.RegularExpressions.Regex.Split(line, @"\s+");
                    // Format: TCP 0.0.0.0:8000 0.0.0.0:0 LISTENING 11276
                    if (parts.Length < 5) continue;
                    string local = parts[1];
                    if (!local.EndsWith(":" + port, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(parts[4], out int pid) && pid > 0)
                        return pid;
                }
            }
            catch (Exception ex)
            {
                _log.Debug(LogCategory.PythonServer, $"GetListeningPid failed: {ex.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Kill a process and its entire child tree (uvicorn workers, etc.) on Windows.
        /// Uses taskkill /T /F; falls back to Process.Kill if taskkill fails.
        /// </summary>
        private void KillProcessTree(int pid)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {pid} /T /F",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                _log.Warning(LogCategory.PythonServer, $"taskkill failed for PID {pid}: {ex.Message} — falling back to Process.Kill");
                try
                {
                    var proc = Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                }
                catch { /* process already gone */ }
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
