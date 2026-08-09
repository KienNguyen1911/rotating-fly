using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Façade layer giữa callers (WinUI ViewModels, Pipeline hooks) và
    /// <see cref="ITaskHistoryStore"/> (SQLite persistence).
    ///
    /// Lịch sử v1 cũ (HistoryService file-based JSON, không còn dùng) đã được
    /// thay thế hoàn toàn bởi SQLite store. Class này tồn tại để:
    ///   1. Giữ API surface cho code đã inject HistoryService qua DI
    ///      (App.xaml.cs, GeminiViewModel) mà không phải sửa constructor.
    ///   2. Chuyển đổi <see cref="HistoryTaskModel"/> (legacy model) ↔
    ///      <see cref="TaskRunHistoryEntry"/> (v2 model) khi cần backward-compat.
    ///
    /// Theo chốt scope với user: KHÔNG migrate data từ JSON cũ sang SQLite.
    /// DB mới bắt đầu trống; entries cũ trong folder <c>history/*.json</c> sẽ
    /// được để nguyên cho user tham khảo nếu cần (không tự xóa).
    /// </summary>
    public class HistoryService
    {
        private readonly ITaskHistoryStore _store;
        private readonly Action<string> _log;

        // Legacy fields — kept only so existing DI consumers don't crash if they
        // ever touch these accessors. New code should use the TaskRunHistoryEntry APIs.
        private static readonly SemaphoreSlim _historyFileSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Constructor mới — dùng SQLite store làm backing storage.
        /// </summary>
        public HistoryService(ITaskHistoryStore store, Action<string>? log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _log = log ?? (_ => { });
        }

        // ─────────────────────────────────────────────────────
        //  New SQLite-backed API (preferred)
        // ─────────────────────────────────────────────────────

        public Task<string> StartTaskRunAsync(TaskRunHistoryEntry entry, CancellationToken ct = default)
            => _store.StartAsync(entry, ct);

        public Task FinishTaskRunAsync(string id, HistoryTaskStatus finalStatus, string? errorMessage = null, string? logsSummary = null, CancellationToken ct = default)
            => _store.MarkFinishedAsync(id, finalStatus, errorMessage, logsSummary, ct);

        public Task AppendAssetPathsAsync(string id, IEnumerable<string> assetPaths, CancellationToken ct = default)
            => _store.AppendAssetPathsAsync(id, assetPaths, ct);

        public Task<List<TaskRunHistoryEntry>> LoadAllRunsAsync(CancellationToken ct = default)
            => _store.LoadAllAsync(ct);

        public Task DeleteRunAsync(string id, CancellationToken ct = default)
            => _store.DeleteAsync(id, ct);

        public Task ClearAllRunsAsync(CancellationToken ct = default)
            => _store.ClearAllAsync(ct);

        // ─────────────────────────────────────────────────────
        //  Legacy API — kept for backward compat with HistoryViewModel v1
        //  (giữ method signature cũ; trả về empty list nếu store trống).
        // ─────────────────────────────────────────────────────

        private string GetHistoryDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
        }

        /// <summary>
        /// Legacy: saves or updates a task entry in the history file for its creation date.
        /// Now a no-op — entries được lưu vào SQLite thông qua StartTaskRunAsync/FinishTaskRunAsync.
        /// Giữ method để code cũ không crash; log một dòng warning để biết là dead path.
        /// </summary>
        public async Task SaveTaskToHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                _log("[WARN] HistoryService.SaveTaskToHistoryAsync (legacy JSON) đã deprecated. Dùng StartTaskRunAsync/FinishTaskRunAsync thay thế.");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }

        /// <summary>Legacy: no-op stub. SQLite entries đã có DeleteRunAsync.</summary>
        public async Task DeleteTaskFromHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                _log("[WARN] HistoryService.DeleteTaskFromHistoryAsync (legacy JSON) đã deprecated. Dùng DeleteRunAsync thay thế.");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }

        /// <summary>
        /// Legacy: returns date strings cho dates có history files. Vì SQLite
        /// không chia theo file theo ngày, trả về list các date strings UNIQUE
        /// từ các entries đã load — đủ để HistoryViewModel v1 hiển thị filter.
        /// </summary>
        public async Task<List<string>> LoadHistoryDatesAsync()
        {
            var entries = await _store.LoadAllAsync();
            return entries
                .Select(e => e.StartedAt.ToString("yyyy-MM-dd"))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(d => d)
                .ToList();
        }

        /// <summary>Legacy sync wrapper, kept for non-async callers (HistoryViewModel.LoadHistory).</summary>
        public List<string> LoadHistoryDates()
        {
            try
            {
                return LoadHistoryDatesAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _log($"[ERROR] LoadHistoryDates: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Legacy: loads tasks for a specific date string (yyyy-MM-dd).
        /// Convert entries → HistoryTaskModel để tương thích binding cũ.
        /// </summary>
        public List<HistoryTaskModel> LoadHistoryTasksForDate(string date)
        {
            var tasks = new List<HistoryTaskModel>();
            try
            {
                var entries = _store.LoadAllAsync().GetAwaiter().GetResult();
                foreach (var e in entries.Where(e => e.StartedAt.ToString("yyyy-MM-dd") == date))
                {
                    tasks.Add(ConvertToLegacyModel(e));
                }
            }
            catch (Exception ex)
            {
                _log($"[ERROR] LoadHistoryTasksForDate({date}): {ex.Message}");
            }
            return tasks;
        }

        private static HistoryTaskModel ConvertToLegacyModel(TaskRunHistoryEntry e)
        {
            return new HistoryTaskModel
            {
                Id = Guid.TryParse(e.Id, out var g) ? g : Guid.NewGuid(),
                VideoUrl = e.ProjectName ?? string.Empty,
                Status = e.Status switch
                {
                    HistoryTaskStatus.Success => "SUCCESS",
                    HistoryTaskStatus.Failed => "FAILED",
                    HistoryTaskStatus.Cancelled => "CANCELLED",
                    HistoryTaskStatus.Running => "RUNNING",
                    _ => "UNKNOWN",
                },
                CreatedAt = e.StartedAt,
                Logs = e.LogsSummary ?? string.Empty,
                TargetLanguage = string.Empty,
                VoiceId = string.Empty,
            };
        }
    }
}
