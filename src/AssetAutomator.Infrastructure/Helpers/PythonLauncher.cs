using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Infrastructure.Helpers
{
    /// <summary>
    /// Orchestrates the lifecycle of the wiltodelta Python watermark-removal
    /// stack:
    ///   1. Resolves a Python executable (embedded at
    ///      <c>tools/PythonEmbed/python.exe</c>, or system PATH, or a
    ///      user-supplied override).
    ///   2. Verifies the <c>remove-ai-watermarks</c> package is importable
    ///      (one-time <c>pip install</c> if missing).
    ///   3. Caches the resolution so <see cref="PythonWatermarkRemover"/>
    ///      doesn't have to re-probe on every image.
    /// </summary>
    /// <remarks>
    /// Mirrors the App-scoped singleton pattern of
    /// <see cref="GoogleFlow2ServerLauncher"/>. The actual install is
    /// driven by <c>tools/Scripts/Setup-PythonEmbed.ps1</c> for first-run
    /// setup; this launcher only does a one-time pip install if the
    /// package is unexpectedly missing (e.g. user wiped site-packages).
    /// </remarks>
    public sealed class PythonLauncher
    {
        private readonly ILogService _log;
        private readonly IConfigService _config;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _initialized;
        private string? _pythonExe;
        private string? _packageVersion;

        public PythonLauncher(ILogService logService, IConfigService configService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Absolute path to the python.exe that will be used. Null until
        /// <see cref="EnsureInstalledAsync"/> has run successfully.
        /// </summary>
        public string? PythonExe => _pythonExe;

        /// <summary>
        /// Version of the <c>remove-ai-watermarks</c> package as reported
        /// by <c>python -m pip show</c>. Null until successfully probed.
        /// </summary>
        public string? PackageVersion => _packageVersion;

        /// <summary>
        /// True if <see cref="EnsureInstalledAsync"/> has completed
        /// successfully at least once.
        /// </summary>
        public bool IsReady => _initialized && _pythonExe != null;

        /// <summary>
        /// Idempotent — safe to call from startup, retry-on-fail, and
        /// per-batch hot paths. Resolves Python first, then probes
        /// <c>remove-ai-watermarks</c> importability, and installs it on
        /// the fly if missing.
        /// </summary>
        /// <returns>(Success, Diagnostics). Never throws.</returns>
        public async Task<(bool Success, string Diagnostics)> EnsureInstalledAsync(
            CancellationToken ct = default)
        {
            if (IsReady) return (true, $"Ready (remove-ai-watermarks v{_packageVersion})");

            await _initLock.WaitAsync(ct);
            try
            {
                if (IsReady) return (true, $"Ready (remove-ai-watermarks v{_packageVersion})");

                // Step 1: Resolve a Python interpreter.
                string? pythonExe = ResolvePythonExe();
                if (pythonExe == null)
                {
                    _log.Warning(LogCategory.General, "Python not found.");
                    return (false,
                        "Python chưa được cài. Chạy tools/Scripts/Setup-PythonEmbed.ps1 một lần để cài Python embedded + remove-ai-watermarks.");
                }

                // Step 2: Probe the package. If missing, install on the fly.
                var (probeOk, probeDiag, version) = await ProbePackageAsync(pythonExe, ct);
                if (!probeOk)
                {
                    _log.Warning(LogCategory.General,
                        $"remove-ai-watermarks probe failed: {probeDiag}. Attempting pip install...");
                    var (installOk, installDiag) = await InstallPackageAsync(pythonExe, ct);
                    if (!installOk)
                    {
                        return (false, installDiag);
                    }
                    // Re-probe after install.
                    (probeOk, probeDiag, version) = await ProbePackageAsync(pythonExe, ct);
                    if (!probeOk)
                    {
                        return (false, probeDiag);
                    }
                }

                _pythonExe = pythonExe;
                _packageVersion = version;
                _initialized = true;
                _log.Success(LogCategory.General,
                    $"remove-ai-watermarks ready (v{version}, python={pythonExe})");
                return (true, $"remove-ai-watermarks v{version} ready");
            }
            catch (Exception ex)
            {
                return (false, $"Launcher init failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Resolves the Python executable in this priority:
        ///   1. <c>tools/PythonEmbed/python.exe</c> next to the WinUI exe
        ///      (the bundle created by Setup-PythonEmbed.ps1). This is
        ///      the default and the recommended runtime.
        ///   2. <c>python</c> / <c>python3</c> on PATH (system install).
        /// The user-facing override was removed because the embedded
        /// Python is always the right choice — it ships with the right
        /// dependencies and is co-installed with the app.
        /// </summary>
        private string? ResolvePythonExe()
        {
            // 1) Embedded next to the WinUI exe.
            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "tools", "PythonEmbed", "python.exe"),
                Path.Combine(baseDir, "tools", "PythonEmbed", "python3.exe"),
                Path.Combine(baseDir, "PythonEmbed", "python.exe"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            // 2) System PATH fallback.
            string? onPath = TryFindOnPath("python") ?? TryFindOnPath("python3");
            if (onPath != null) return onPath;

            return null;
        }

        private async Task<(bool Ok, string Diagnostics, string Version)> ProbePackageAsync(
            string pythonExe, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "-c \"import remove_ai_watermarks.cli as _c; import importlib.metadata as m; print(m.version('remove-ai-watermarks'))\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Failed to spawn python for package probe", string.Empty);

                string stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                string stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);

                if (proc.ExitCode != 0)
                {
                    return (false,
                        $"remove-ai-watermarks chưa cài ({Truncate(stderr.Trim(), 100)})",
                        string.Empty);
                }

                string version = stdout.Trim();
                if (string.IsNullOrEmpty(version)) version = "unknown";
                return (true, "ok", version);
            }
            catch (OperationCanceledException)
            {
                return (false, "Python probe timed out (30s)", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Python probe exception: {ex.GetType().Name}: {ex.Message}", string.Empty);
            }
        }

        private async Task<(bool Ok, string Diagnostics)> InstallPackageAsync(
            string pythonExe, CancellationToken ct)
        {
            try
            {
                _log.Info(LogCategory.General, "Installing remove-ai-watermarks[visible] via pip...");
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "-m pip install --upgrade \"remove-ai-watermarks[visible]\" --disable-pip-version-check",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMinutes(5));

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Failed to spawn pip");

                string stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                string stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);

                if (proc.ExitCode != 0)
                {
                    return (false,
                        $"pip install thất bại: {Truncate(stderr.Trim(), 200)}");
                }
                _ = stdout; // currently unused; kept for debug logging
                return (true, "remove-ai-watermarks[visible] installed");
            }
            catch (OperationCanceledException)
            {
                return (false, "pip install timed out (5 min)");
            }
            catch (Exception ex)
            {
                return (false, $"pip install exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string? TryFindOnPath(string exe)
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                try
                {
                    string full = Path.Combine(dir, exe + ".exe");
                    if (File.Exists(full)) return full;
                    full = Path.Combine(dir, exe);
                    if (File.Exists(full)) return full;
                }
                catch { /* skip */ }
            }
            return null;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
