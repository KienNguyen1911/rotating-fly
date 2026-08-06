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

            // Walk up from BaseDirectory (dev scenario).
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "tools", "PythonEmbed", "python.exe");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            // Fall back to system python only — never use GetCurrentDirectory().
            return "python";
        }

        public string ResolveServerScriptPath()
        {
            // SECURITY: Only resolve to paths inside the app BaseDirectory tree.
            // Never fall back to Directory.GetCurrentDirectory() as that can be
            // any arbitrary location and could cause path injection issues.
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "Modules", "Gemini-API-2.0.0", "server.py");
            if (File.Exists(scriptPath))
                return scriptPath;

            // Walk up from BaseDirectory (dev scenario: exe in bin/Debug sub-folder).
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "Modules", "Gemini-API-2.0.0", "server.py");
                if (File.Exists(candidate))
                    return candidate;
                candidate = Path.Combine(dir.FullName, "src", "Modules", "Gemini-API-2.0.0", "server.py");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            // Last resort — return the canonical BaseDirectory path even if not found.
            // Callers should check File.Exists() before using.
            return scriptPath;
        }

        public async Task<(bool Success, string Diagnostics)> RestartServerAsync(string baseUrl = "http://localhost:8000")
        {
            _log.Info(LogCategory.PythonServer, "Restarting Gemini Python Server...");
            StopServer();
            // Wait long enough for Windows to release the socket (TIME_WAIT).
            // 1.5s is empirically enough on Win11 for port 8000 in our setup.
            await Task.Delay(1500);
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
                // Always start by killing the C#-tracked process tree (the parent
                // python.exe that launched uvicorn workers).
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _log.Info(LogCategory.PythonServer, $"Killing tracked server process (PID: {_serverProcess.Id})...");
                    try
                    {
                        _serverProcess.Kill(entireProcessTree: true);
                        _serverProcess.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(LogCategory.PythonServer, $"Failed to kill tracked process: {ex.Message}");
                    }
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }

                // ── Belt & suspenders: also nuke ANY python.exe process still
                // listening on port 8000. Without this step, an orphaned child
                // from a previous session can hold the port and the new
                // server fails with `address already in use`, leaving us
                // talking to a stale, unauthenticated server.
                int port = 8000;
                if (TryParsePortFromBaseUrl(baseUrl: "http://localhost:8000", out var configuredPort))
                    port = configuredPort;
                KillOrphanedListenersOnPort(port);
            }
            catch (Exception ex)
            {
                _log.Error(LogCategory.PythonServer, $"Error stopping server: {ex.Message}");
            }
        }

        private void KillOrphanedListenersOnPort(int port)
        {
            try
            {
                // netstat -ano | findstr :PORT  →  PID list
                var psi = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano -p TCP",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);

                var pids = new HashSet<int>();
                string needle = $":{port} ";
                foreach (var line in output.Split('\n'))
                {
                    if (!line.Contains(needle)) continue;
                    // Columns: Proto Local-Address Foreign-Address State PID
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("TCP") == false) continue;
                    var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;
                    // Only LISTENING sockets matter for "address already in use".
                    if (!parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(parts[4], out int pid))
                        pids.Add(pid);
                }

                foreach (int pid in pids)
                {
                    if (pid <= 4) continue; // skip system PIDs (0/4)
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        // Only kill python.exe / uvicorn / server.py — never blindly
                        // kill whatever happens to listen on 8000 (could be a dev tool).
                        string name = p.ProcessName.ToLowerInvariant();
                        bool isPythonish = name.Contains("python") || name.Contains("uvicorn");
                        if (!isPythonish)
                        {
                            _log.Debug(LogCategory.PythonServer,
                                $"Skipping PID {pid} ({p.ProcessName}) on port {port} — not python.exe");
                            continue;
                        }

                        _log.Info(LogCategory.PythonServer,
                            $"Killing orphan listener on port {port}: PID {pid} ({p.ProcessName})");
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(2000);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning(LogCategory.PythonServer,
                            $"Failed to kill orphan PID {pid} on port {port}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warning(LogCategory.PythonServer, $"KillOrphanedListenersOnPort failed: {ex.Message}");
            }
        }

        private static bool TryParsePortFromBaseUrl(string baseUrl, out int port)
        {
            port = 0;
            try
            {
                var uri = new Uri(baseUrl);
                port = uri.Port;
                return port > 0;
            }
            catch { return false; }
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
