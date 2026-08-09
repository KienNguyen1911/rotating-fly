using System;
using System.Threading;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Static-friendly queue around <see cref="WatermarkParallelPool"/>. The
    /// provider stack (e.g. <c>FlowLocalImageGenProvider</c>) is instantiated
    /// via <c>new</c> in the factory and cannot receive DI directly, so we
    /// expose a simple out-of-band registration entry point.
    ///
    /// When the pool hasn't been registered, <see cref="IsAvailable"/>
    /// returns <c>false</c> and callers MUST treat it as a no-op (preserves
    /// legacy behavior for unit tests / minimal builds).
    /// </summary>
    public sealed class WatermarkRemovalQueue
    {
        private readonly IWatermarkRemover _remover;
        private readonly int _maxParallel;

        /// <summary>
        /// Currently-active pool. Created once (lazily) on first Enqueue to
        /// avoid spawning the SemaphoreSlim until we actually need it.
        /// </summary>
        private WatermarkParallelPool? _pool;

        public WatermarkRemovalQueue(IWatermarkRemover remover, int maxParallel)
        {
            _remover = remover ?? throw new ArgumentNullException(nameof(remover));
            _maxParallel = maxParallel;
        }

        /// <summary>
        /// True if the queue is ready to accept work. Becomes true on the first
        /// call to <see cref="Enqueue"/>; the underlying pool is lazy.
        /// </summary>
        public bool IsAvailable => _remover != null;

        /// <summary>
        /// Probe the underlying CLI/health. Convenience for the WinUI
        /// "Watermark" status indicator.
        /// </summary>
        public Task<WatermarkRemoverStatus> ProbeAsync(CancellationToken ct = default)
            => _remover.ProbeAsync(ct);

        /// <summary>
        /// Fire-and-forget watermark removal for the given image. Returns
        /// immediately; the result is written back to <paramref name="item"/>
        /// on the UI synchronization context asynchronously.
        /// </summary>
        /// <param name="item">The batch item whose properties will be updated when done.</param>
        /// <param name="imagePath">Absolute path to the just-generated image.</param>
        /// <param name="uiContext">SynchronizationContext from the UI thread (or null).</param>
        public void Enqueue(
            BatchImageItem item,
            string imagePath,
            SynchronizationContext? uiContext = null)
        {
            if (_remover == null || item == null || string.IsNullOrWhiteSpace(imagePath)) return;

            if (_pool == null)
            {
                _pool = new WatermarkParallelPool(_remover, _maxParallel);
            }
            _pool.Enqueue(item, imagePath, uiContext);
        }

        /// <summary>
        /// Wait for all in-flight removals to finish. Used by the
        /// "Remove Watermark" batch button so the user gets a final progress
        /// notification.
        /// </summary>
        public Task WaitForCompletionAsync(TimeSpan? timeout = null)
            => _pool?.WaitForCompletionAsync(timeout) ?? Task.CompletedTask;
    }
}
