using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using WMResult = AssetAutomator.Core.Models.WatermarkResult;

namespace AssetAutomator.Infrastructure.Helpers
{
    /// <summary>
    /// Concrete <see cref="IWatermarkRemover"/> that spawns the
    /// <c>remove-ai-watermarks</c> Python CLI (wiltodelta) for visible-mark
    /// removal via inpainting (OpenCV / MI-GAN / LaMa). Falls back to the
    /// <c>erase --region</c> subcommand when the user has drawn an explicit
    /// region override.
    ///
    /// Replaces the earlier <c>@pilio/gemini-watermark-remover</c> CLI
    /// because the math-only reverse-alpha pipeline is brittle on images
    /// whose watermark is in a non-catalog position (e.g. centre-bottom for
    /// certain Flow Local renders, or any image that has been re-encoded).
    /// Inpainting is robust to layout drift at the cost of being slightly
    /// "soft" on very small logos.
    /// </summary>
    /// <remarks>
    /// Failure model: never throws. Returns
    /// <see cref="WMResult.Skipped"/> with a human-readable reason on
    /// any error path (Python missing, package missing, timeout, non-zero
    /// exit, missing file).
    /// </remarks>
    public sealed class PythonWatermarkRemover : IWatermarkRemover
    {
        private readonly PythonLauncher _launcher;
        private readonly ILogService _log;
        private readonly IConfigService _config;

        public PythonWatermarkRemover(
            PythonLauncher launcher,
            ILogService logService,
            IConfigService configService)
        {
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public async Task<WMResult> RemoveAsync(string imagePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return WMResult.Skipped("Đường dẫn ảnh rỗng");
            }
            if (!File.Exists(imagePath))
            {
                return WMResult.Skipped($"Không tìm thấy file: {Path.GetFileName(imagePath)}");
            }

            // Ensure the launcher is ready. If Python or the package is
            // missing, the launcher returns (false, "...") and we surface
            // that as the result reason without throwing.
            var (ready, diag) = await _launcher.EnsureInstalledAsync(ct);
            if (!ready)
            {
                return WMResult.Skipped(diag);
            }

            string pythonExe = _launcher.PythonExe!;
            var settings = _config.CurrentSettings;

            // Auto-detect path: wiltodelta's `visible` subcommand finds
            // registered Gemini marks and removes them. The CLI does
            // NOT expose --mark flags we need to pass for normal use;
            // `--mark auto` scans every registered provider and removes
            // every detected match in one pass.
            bool useErase = false; // region override removed — not user-facing
            int timeoutSec = settings.WatermarkPerImageTimeoutSec > 0
                ? settings.WatermarkPerImageTimeoutSec : 30;

            try
            {
                // Region override was removed from the UI because the
                // auto-detector is reliable on standard Gemini watermarks.
                // The `erase --region` subcommand is still available in
                // the Python CLI for one-off recovery via terminal, but
                // is not exposed in the WinUI settings anymore.
                var cliArgs = BuildVisibleArgs(imagePath, settings);

                string pyDir = Path.GetDirectoryName(pythonExe) ?? string.Empty;
                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"-m remove_ai_watermarks.cli {cliArgs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(imagePath) ?? string.Empty,
                };

                if (!string.IsNullOrEmpty(pyDir))
                {
                    string existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                    psi.EnvironmentVariables["PATH"] = pyDir + Path.PathSeparator + existingPath;
                    // Suppress the embedded Python's site.py banner that
                    // would otherwise pollute the JSON stdout.
                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                    psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                }

                var sw = Stopwatch.StartNew();
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return WMResult.Skipped("Không thể spawn python process");
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

                try
                {
                    string stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                    string stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
                    await proc.WaitForExitAsync(cts.Token);
                    sw.Stop();

                    if (proc.ExitCode != 0)
                    {
                        _log.Warning(LogCategory.General,
                            $"remove-ai-watermarks exit {proc.ExitCode} for {Path.GetFileName(imagePath)}: {stderr.Trim()}");
                        return WMResult.Skipped(
                            $"Python CLI exit {proc.ExitCode}: {Truncate(stderr, 160)}");
                    }

                    return ParseCliOutput(stdout, sw.ElapsedMilliseconds, imagePath);
                }
                catch (OperationCanceledException)
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* noop */ }
                    if (ct.IsCancellationRequested)
                    {
                        return WMResult.Skipped("Đã hủy");
                    }
                    return WMResult.Skipped($"Timeout ({timeoutSec}s)");
                }
            }
            catch (Exception ex)
            {
                _log.Warning(LogCategory.General,
                    $"Watermark removal threw for {Path.GetFileName(imagePath)}: {ex.Message}");
                return WMResult.Skipped($"Exception: {ex.GetType().Name}");
            }
        }

        /// <summary>
        /// Builds the arg string for the auto-detecting <c>visible</c>
        /// subcommand. Uses the user-selected inpainting backend; falls
        /// back to <c>cv2</c> (OpenCV) if the setting is empty or unknown.
        /// </summary>
        /// <remarks>
        /// Flag notes (verified against remove-ai-watermarks ≥ 0.26):
        ///   - The CLI does NOT support <c>--overwrite</c>; it just writes
        ///     to <c>-o</c> unconditionally.
        ///   - The CLI does NOT support <c>--json</c>; status goes to
        ///     stdout as human-readable text. We parse the exit code +
        ///     a couple of sentinel lines instead.
        ///   - The CLI supports <c>--mark auto|gemini|...</c> to scope
        ///     detection to one provider. We always pass <c>auto</c> so
        ///     every detected mark is removed in one pass.
        /// </remarks>
        private static string BuildVisibleArgs(string imagePath, AppSettings settings)
        {
            var sb = new StringBuilder(256);
            sb.Append("visible ");
            sb.Append(QuoteArg(imagePath));
            sb.Append(" -o ").Append(QuoteArg(imagePath));
            sb.Append(" --mark auto");
            sb.Append(" --backend ").Append(NormalizeBackend(settings.WatermarkInpaintBackend));
            return sb.ToString();
        }

        /// <summary>
        /// Maps the user-facing backend name (opencv / migan / lama) to
        /// the CLI's vocabulary (cv2 / migan / lama). <c>auto</c> is
        /// preserved as-is so the CLI picks the best installed backend.
        /// </summary>
        private static string NormalizeBackend(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "auto";
            return raw.Trim().ToLowerInvariant() switch
            {
                "opencv" or "cv2" or "cv" => "cv2",
                "migan" or "mi-gan" or "mi_gan" => "migan",
                "lama" or "la-ma" or "la_ma" => "lama",
                "auto" => "auto",
                _ => "auto",
            };
        }

        private static string QuoteArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return "\"\"";
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        public async Task<WatermarkRemoverStatus> ProbeAsync(CancellationToken ct = default)
        {
            var (ready, diag) = await _launcher.EnsureInstalledAsync(ct);
            if (!ready)
            {
                return WatermarkRemoverStatus.NotReady(diag);
            }
            return WatermarkRemoverStatus.Ready(_launcher.PackageVersion ?? "unknown");
        }

        /// <summary>
        /// Parses the CLI's stdout. The wiltodelta CLI does NOT support
        /// <c>--json</c>; output is human-readable text. We accept the
        /// invocation as successful if:
        ///   - exit code is 0
        ///   - stdout contains a "Saved:" line (the CLI's success marker)
        /// We treat output as a soft failure if:
        ///   - exit code is non-zero (handled by caller)
        ///   - stdout does NOT contain "Saved:" (process succeeded but
        ///     the mark was not actually erased; e.g. <c>--mark gemini</c>
        ///     found nothing to do and skipped the write).
        /// </summary>
        private static WMResult ParseCliOutput(string stdout, long durationMs, string imagePath)
        {
            // The CLI prints "Saved: <path>" (with two leading spaces) on
            // success. The exact line we look for is "Saved:" — this works
            // across recent 0.26.x revisions.
            if (stdout.Contains("Saved:", StringComparison.Ordinal))
            {
                return WMResult.Clean("cv2-inpaint", durationMs);
            }

            // If the CLI exited 0 but didn't write, the most common cause
            // is "no mark detected". Surface that as a Skipped result with
            // a useful reason so the UI can show it.
            string trimmed = stdout.Trim();
            if (trimmed.Contains("No mark detected", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("no watermark", StringComparison.OrdinalIgnoreCase))
            {
                return WMResult.Skipped("Không tìm thấy mark nào để xóa");
            }
            return WMResult.Skipped("Python CLI không xác nhận đã xóa mark");
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
