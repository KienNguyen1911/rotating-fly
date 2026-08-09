using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using WMResult = AssetAutomator.Core.Models.WatermarkResult;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Fan-out pool for watermark removal. Accepts many (item, imagePath) pairs
    /// and processes them concurrently, bounded by <see cref="MaxParallel"/>.
    /// Results are pushed back to the originating <see cref="BatchImageItem"/>
    /// via the supplied <see cref="SynchronizationContext"/> (typically the UI's)
    /// so the HistoryPage badge updates correctly.
    /// </summary>
    /// <remarks>
    /// Kept in the Application layer (BCL-only) so the pool has no WinUI
    /// dependency. The WinUI page passes its <c>SynchronizationContext.Current</c>
    /// via the Enqueue helper; the pool just marshals property updates through
    /// it without knowing the underlying dispatcher type.
    /// </remarks>
    public sealed class WatermarkParallelPool
    {
        private readonly IWatermarkRemover _remover;
        private readonly SemaphoreSlim _throttle;
        private readonly ConcurrentBag<RunRecord> _active = new();
        private int _inFlightCount;
        private bool _disposed;

        /// <summary>
        /// Max concurrent watermark-removal subprocesses. Defaults to
        /// <c>max(2, ProcessorCount / 2)</c> so we don't saturate the disk
        /// with parallel sharp decodes.
        /// </summary>
        public int MaxParallel { get; }

        public WatermarkParallelPool(IWatermarkRemover remover, int? maxParallel = null)
        {
            _remover = remover ?? throw new ArgumentNullException(nameof(remover));
            int n = maxParallel ?? Math.Max(2, Environment.ProcessorCount / 2);
            // Clamp to sane range.
            if (n < 1) n = 1;
            if (n > 8) n = 8;
            MaxParallel = n;
            _throttle = new SemaphoreSlim(n, n);
        }

        /// <summary>
        /// Enqueue a watermark-removal task. Returns immediately. The result
        /// is posted back to <paramref name="item"/> on the supplied
        /// synchronization context (UI thread).
        /// </summary>
        /// <param name="item">The batch item whose <see cref="BatchImageItem.WatermarkRemoved"/>
        /// and <see cref="BatchImageItem.WatermarkNote"/> properties will be updated.</param>
        /// <param name="imagePath">Absolute path to the just-generated image.</param>
        /// <param name="uiContext">SynchronizationContext from the UI thread (e.g. <c>SynchronizationContext.Current</c>
        /// captured on the WinUI page). May be null in non-UI callers.</param>
        /// <param name="ct">Cancellation token honored when the pool is shutting down.</param>
        public void Enqueue(
            BatchImageItem item,
            string imagePath,
            SynchronizationContext? uiContext = null,
            CancellationToken ct = default)
        {
            if (_disposed) return;
            if (item == null) return;

            Interlocked.Increment(ref _inFlightCount);
            var record = new RunRecord(item, imagePath, uiContext);
            _active.Add(record);

            // Fire-and-forget. The ThrottleRun method itself handles errors.
            _ = Task.Run(() => ThrottleRunAsync(record, ct), ct);
        }

        private async Task ThrottleRunAsync(RunRecord record, CancellationToken ct)
        {
            try
            {
                try
                {
                    await _throttle.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    var result = await _remover.RemoveAsync(record.ImagePath, ct).ConfigureAwait(false);
                    ApplyResult(record, result);
                }
                catch (OperationCanceledException)
                {
                    ApplyResult(record, WMResult.Skipped("Đã hủy"));
                }
                catch (Exception ex)
                {
                    ApplyResult(record, WMResult.Skipped($"Pool exception: {ex.GetType().Name}"));
                }
                finally
                {
                    _throttle.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _inFlightCount);
            }
        }

        private static void ApplyResult(RunRecord record, WMResult result)
        {
            void Apply()
            {
                record.Item.WatermarkRemoved = result.Applied;
                record.Item.WatermarkNote = result.Applied
                    ? $"Watermark removed ({result.DecisionTier}, {result.DurationMs}ms)"
                    : result.Reason;

                // Only the success path mutates the on-disk file (the
                // remover writes back to the same path via the CLI's
                // `-o` flag). When the rewrite actually happens we must
                // force the thumbnail to reload — WinUI's BitmapImage
                // caches decoded pixels keyed on the source URI, and
                // since `ImagePath` is unchanged the XAML binding alone
                // would not re-evaluate. Bumping `ImageCacheVersion`
                // changes the rendered `ImageCacheKey`, which busts the
                // cache. We unconditionally bump on Applied=true; the
                // race with a manual file rewrite by another tool is
                // harmless (just one extra reload).
                if (result.Applied)
                {
                    record.Item.BumpImageCacheVersion();
                }
            }

            if (record.UiContext != null && SynchronizationContext.Current != record.UiContext)
            {
                // Marshal back to UI thread before mutating the ObservableObject's
                // bindable properties. WinUI throws on cross-thread updates.
                record.UiContext.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        /// <summary>
        /// Wait for all in-flight removals to finish. Used by the "Remove Watermark"
        /// batch button so the user gets a final progress notification.
        /// </summary>
        /// <remarks>
        /// Polls the in-flight task count and throttle permits. Closes after <paramref name="timeout"/>.
        /// </remarks>
        public async Task WaitForCompletionAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
            while (DateTime.UtcNow < deadline)
            {
                if (Volatile.Read(ref _inFlightCount) == 0 && _throttle.CurrentCount == MaxParallel)
                {
                    // All enqueued jobs done and all permits free.
                    return;
                }
                await Task.Delay(100);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _throttle.Dispose();
        }

        private sealed class RunRecord
        {
            public BatchImageItem Item { get; }
            public string ImagePath { get; }
            public SynchronizationContext? UiContext { get; }

            public RunRecord(BatchImageItem item, string imagePath,
                SynchronizationContext? uiContext)
            {
                Item = item;
                ImagePath = imagePath;
                UiContext = uiContext;
            }
        }
    }
}
