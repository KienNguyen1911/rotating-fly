using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AssetAutomator.Helpers
{
    /// <summary>
    /// Manages launching and ensuring health of the embedded Python Gemini WebAPI Server (server.py).
    /// Prefers embedded Python at PythonEmbed/python.exe if available.
    /// </summary>
    public static class PythonServerManager
    {
        private static Process? _serverProcess;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        /// <summary>
        /// Checks if the Gemini API Server (http://localhost:8000/api/health) is running.
        /// </summary>
        public static async Task<bool> IsServerRunningAsync(string baseUrl = "http://localhost:8000")
        {
            try
            {
                string healthUrl = baseUrl.TrimEnd('/') + "/api/health";
                var response = await _httpClient.GetAsync(healthUrl);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures the Python server is running. Launches server.py in background if needed.
        /// </summary>
        public static async Task<bool> EnsureServerRunningAsync(string baseUrl = "http://localhost:8000")
        {
            if (await IsServerRunningAsync(baseUrl))
            {
                return true;
            }

            // Find Python executable (prefer PythonEmbed/python.exe)
            string pythonExe = ResolvePythonExecutable();
            string serverScriptPath = ResolveServerScriptPath();

            if (!File.Exists(serverScriptPath))
            {
                Trace.WriteLine($"[PythonServerManager] server.py not found at: {serverScriptPath}");
                return false;
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
                string scriptDir = Path.GetDirectoryName(serverScriptPath)!;
                string srcDir = Path.Combine(scriptDir, "src");
                startInfo.EnvironmentVariables["PYTHONPATH"] = srcDir + ";" + (Environment.GetEnvironmentVariable("PYTHONPATH") ?? "");

                var settings = ConfigService.CurrentSettings;
                if (!string.IsNullOrEmpty(settings.ChromeProfilesDir))
                {
                    startInfo.EnvironmentVariables["CHROME_PROFILES_DIR"] = settings.ChromeProfilesDir;
                }
                if (!string.IsNullOrEmpty(settings.DefaultChromeProfile))
                {
                    startInfo.EnvironmentVariables["DEFAULT_CHROME_PROFILE"] = settings.DefaultChromeProfile;
                }

                _serverProcess = new Process { StartInfo = startInfo };
                _serverProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Trace.WriteLine($"[GeminiServer] {e.Data}"); };
                _serverProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Trace.WriteLine($"[GeminiServer] {e.Data}"); };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                // Wait up to 30 seconds for server to start
                for (int i = 0; i < 60; i++)
                {
                    await Task.Delay(500);
                    if (await IsServerRunningAsync(baseUrl))
                    {
                        Trace.WriteLine("[PythonServerManager] Server successfully launched and responding on /api/health.");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PythonServerManager] Exception while launching python server: {ex.Message}");
            }

            return await IsServerRunningAsync(baseUrl);
        }

        /// <summary>
        /// Resolves path to Python executable, preferring embedded Python.
        /// </summary>
        public static string ResolvePythonExecutable()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string embeddedPythonPath = Path.Combine(baseDir, "PythonEmbed", "python.exe");

            if (File.Exists(embeddedPythonPath))
            {
                return embeddedPythonPath;
            }

            // Check relative to working directory / solution root
            string relativeEmbeddedPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "PythonEmbed", "python.exe"));
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
        public static string ResolveServerScriptPath()
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
        /// </summary>
        public static async Task<bool> RestartServerAsync(string baseUrl = "http://localhost:8000")
        {
            Trace.WriteLine("[PythonServerManager] Restarting Gemini Python Server...");
            StopServer();
            await Task.Delay(1500);
            return await EnsureServerRunningAsync(baseUrl);
        }

        /// <summary>
        /// Stops the background server process safely upon application shutdown or server restart.
        /// </summary>
        public static void StopServer()
        {
            try
            {
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PythonServerManager] Error stopping server process: {ex.Message}");
            }

            // Ensure any orphaned python processes running server.py are terminated
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c wmic process where \"commandline like '%server.py%'\" call terminate",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);
            }
            catch { }
        }
    }
}
