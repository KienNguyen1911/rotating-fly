using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Core.Interfaces
{
    /// <summary>
    /// Removes Gemini AI watermarks from generated images using the
    /// <c>@pilio/gemini-watermark-remover</c> CLI (Reverse Alpha Blending).
    ///
    /// Implementations are expected to be:
    ///   - <b>Failure-tolerant</b>: never throw; return a <see cref="WatermarkResult"/>
    ///     with <c>Applied = false</c> + a human-readable reason instead.
    ///   - <b>Per-image timeout</b>: must not block longer than ~10 seconds for a single
    ///     1024×1024 image (CLI normally completes in ~50ms).
    ///   - <b>Thread-safe</b>: safe to call from multiple thread-pool threads concurrently.
    /// </summary>
    /// <remarks>
    /// Why a separate interface?  The WinUI Presentation layer shouldn't have to
    /// know about Node.js, pnpm, or the spawn-process boundary. The Application
    /// layer only needs to say "remove the watermark from this file" — IMPL
    /// choice (CLI wrapper vs in-process .NET port vs SaaS HTTP) lives in
    /// <c>AssetAutomator.Infrastructure</c>.
    /// </remarks>
    public interface IWatermarkRemover
    {
        /// <summary>
        /// Removes the Gemini watermark from <paramref name="imagePath"/> in-place.
        ///
        /// The CLI overwrites the file directly (no separate output path), so
        /// callers don't need to worry about the temporary file lifecycle.
        /// </summary>
        /// <param name="imagePath">Absolute path to a PNG/JPEG/WebP file produced by Google Flow.</param>
        /// <param name="ct">Cancellation token. Implementations MUST honor cancellation.</param>
        /// <returns>
        /// A <see cref="WatermarkResult"/> describing what happened. Never throws —
        /// failures are surfaced via <see cref="WatermarkResult.Applied"/> = false and
        /// a non-empty <see cref="WatermarkResult.Reason"/>.
        /// </returns>
        Task<WatermarkResult> RemoveAsync(string imagePath, CancellationToken ct = default);

        /// <summary>
        /// Probes whether the underlying CLI/back-end is reachable. Used by the
        /// WinUI startup health-check so we can show a friendly "Node.js missing"
        /// dialog instead of silently failing every batch.
        /// </summary>
        /// <returns>
        /// A status describing CLI availability and version. Never throws.
        /// </returns>
        Task<WatermarkRemoverStatus> ProbeAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Health-status of the watermark-remover back-end.
    /// </summary>
    public sealed class WatermarkRemoverStatus
    {
        public bool IsAvailable { get; init; }
        public string? Version { get; init; }
        public string? Diagnostic { get; init; }

        public static WatermarkRemoverStatus Ready(string version) =>
            new() { IsAvailable = true, Version = version, Diagnostic = $"gwr CLI ready (v{version})" };

        public static WatermarkRemoverStatus NotReady(string reason) =>
            new() { IsAvailable = false, Diagnostic = reason };
    }
}
