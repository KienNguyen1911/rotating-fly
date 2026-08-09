using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Core.Interfaces;

/// <summary>
/// Store lịch sử task dạng SQLite local DB. Persistence nằm trong layer
/// <c>AssetAutomator.Infrastructure</c>; interface này ở Core để Application
/// và WinUI đều reference được mà không kéo theo Microsoft.Data.Sqlite.
///
/// Tất cả method đều thread-safe; implementation dùng internal
/// <see cref="SemaphoreSlim"/> để serialize write access.
///
/// History là append-mostly — <see cref="MarkFinishedAsync"/> chỉ update row
/// đã có (theo <see cref="TaskRunHistoryEntry.Id"/>), không xóa entries cũ.
/// Retention policy "giữ vĩnh viễn" được đảm bảo bằng cách KHÔNG có method
/// auto-prune ở tầng store; user tự xóa thủ công qua UI (xem HistoryViewModel).
/// </summary>
public interface ITaskHistoryStore
{
    /// <summary>
    /// Insert một entry mới với <c>Status = Running</c>. Trả về <c>TaskRunHistoryEntry.Id</c>.
    /// Nếu entry đã tồn tại (cùng Id), update StartedAt + Status = Running.
    /// </summary>
    Task<string> StartAsync(TaskRunHistoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Update entry đã có: set FinishedAt, Status, ErrorMessage, LogsSummary.
    /// Các field khác giữ nguyên.
    /// </summary>
    Task MarkFinishedAsync(string id, HistoryTaskStatus finalStatus, string? errorMessage = null, string? logsSummary = null, CancellationToken ct = default);

    /// <summary>
    /// Update thêm AssetPaths cho entry (vd: BatchImageGen scan folder output
    /// sau khi tất cả ảnh sinh xong).
    /// </summary>
    Task AppendAssetPathsAsync(string id, IEnumerable<string> assetPaths, CancellationToken ct = default);

    /// <summary>
    /// Load toàn bộ entries, sắp xếp StartedAt DESC (mới nhất trước).
    /// Không giới hạn — caller tự filter/paginate nếu cần.
    /// </summary>
    Task<List<TaskRunHistoryEntry>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Xóa 1 entry theo id. Không ảnh hưởng tới các entry khác.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Xóa toàn bộ history. Dùng cho "Clear all history" command trong UI.
    /// </summary>
    Task ClearAllAsync(CancellationToken ct = default);
}
