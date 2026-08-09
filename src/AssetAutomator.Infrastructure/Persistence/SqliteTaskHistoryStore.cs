using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using Microsoft.Data.Sqlite;

namespace AssetAutomator.Infrastructure.Persistence;

/// <summary>
/// SQLite implementation của <see cref="ITaskHistoryStore"/>.
///
/// Vì sao chọn SQLite thuần (Microsoft.Data.Sqlite) thay vì EF Core:
///   - Read-mostly workload: lịch sử chủ yếu được INSERT khi task chạy,
///     SELECT khi user mở History tab. EF Core overhead không tương xứng.
///   - Schema đơn giản (1 table, không có quan hệ) — raw SQL dễ audit hơn.
///   - Bundle size: Microsoft.Data.Sqlite.Core + SQLitePCLRaw chỉ ~5 MB,
///     EF Core SQLite thêm ~10 MB dependencies.
///
/// Thread-safety:
///   - Một <see cref="SemaphoreSlim"/> serializes TẤT CẢ write operations
///     (StartAsync, MarkFinishedAsync, AppendAssetPathsAsync, DeleteAsync, ClearAllAsync).
///   - Read operations (LoadAllAsync) cũng đi qua semaphore để đảm bảo
///     snapshot nhất quán khi concurrent writes đang chạy.
///   - Mỗi operation mở connection riêng (open/close per call) thay vì giữ
///     connection lâu dài. SQLite handle contention qua file-level locking
///     và connection pool của Microsoft.Data.Sqlite.
///
/// Schema migration:
///   - Bảng được tạo on-demand trong constructor nếu chưa tồn tại.
///   - Theo chốt scope với user: KHÔNG migrate data từ JSON history cũ.
///   - DB mới bắt đầu trống.
/// </summary>
public class SqliteTaskHistoryStore : ITaskHistoryStore
{
    private static readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    private readonly string _connectionString;

    // Cache DB path để error message / debug dễ đọc.
    public string DatabasePath { get; }

    public SqliteTaskHistoryStore(string? databasePath = null)
    {
        DatabasePath = string.IsNullOrWhiteSpace(databasePath)
            ? GetDefaultDatabasePath()
            : databasePath!;

        try
        {
            string? dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
        catch
        {
            // Best-effort: nếu không tạo được folder, sẽ fail ở Open() với lỗi rõ ràng hơn.
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        EnsureSchema();
    }

    /// <summary>
    /// %APPDATA%\AssetAutomator\history.db.
    /// Per-user, per-machine. Không roaming để tránh file-lock khi user đăng nhập
    /// nhiều máy cùng lúc.
    /// </summary>
    private static string GetDefaultDatabasePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AssetAutomator", "history.db");
    }

    private void EnsureSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // V1: base table
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS task_runs (
                    id                   TEXT PRIMARY KEY,
                    task_type            INTEGER NOT NULL,
                    project_name         TEXT    NOT NULL,
                    flow_project_id      TEXT,
                    status               INTEGER NOT NULL,
                    started_at           TEXT    NOT NULL,
                    finished_at          TEXT,
                    output_dir           TEXT,
                    asset_paths_json     TEXT,
                    error_message        TEXT,
                    logs_summary         TEXT,
                    scriptwriter_gem_name TEXT,
                    scene_creator_gem_name TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_task_runs_started_at ON task_runs(started_at DESC);
                CREATE INDEX IF NOT EXISTS idx_task_runs_status ON task_runs(status);
            ";
            cmd.ExecuteNonQuery();
        }

        // V2: add gem-name columns nếu bảng đã tồn tại (migration từ v1 cũ)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                ALTER TABLE task_runs ADD COLUMN scriptwriter_gem_name TEXT;
                ALTER TABLE task_runs ADD COLUMN scene_creator_gem_name TEXT;
            ";
            try { cmd.ExecuteNonQuery(); } catch { /* columns có thể đã tồn tại — bỏ qua */ }
        }
    }

    public async Task<string> StartAsync(TaskRunHistoryEntry entry, CancellationToken ct = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Id)) entry.Id = Guid.NewGuid().ToString("N");

        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO task_runs (id, task_type, project_name, flow_project_id, status, started_at, output_dir, asset_paths_json, error_message, logs_summary, scriptwriter_gem_name, scene_creator_gem_name)
                VALUES ($id, $type, $name, $flowId, $status, $startedAt, $outputDir, $assets, $err, $logs, $scriptGem, $sceneGem)
                ON CONFLICT(id) DO UPDATE SET
                    started_at           = excluded.started_at,
                    status               = excluded.status,
                    project_name         = excluded.project_name,
                    task_type            = excluded.task_type,
                    output_dir           = excluded.output_dir,
                    scriptwriter_gem_name  = excluded.scriptwriter_gem_name,
                    scene_creator_gem_name = excluded.scene_creator_gem_name,
                    finished_at          = NULL,
                    error_message        = NULL;
            ";
            BindCommon(cmd, entry);
            cmd.Parameters.AddWithValue("$startedAt", entry.StartedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$status", (int)HistoryTaskStatus.Running);
            cmd.Parameters.AddWithValue("$err", (object?)entry.ErrorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$scriptGem", (object?)entry.ScriptwriterGemName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sceneGem", (object?)entry.SceneCreatorGemName ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            return entry.Id;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkFinishedAsync(string id, HistoryTaskStatus finalStatus, string? errorMessage = null, string? logsSummary = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));

        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE task_runs
                SET status        = $status,
                    finished_at   = $finishedAt,
                    error_message = $err,
                    logs_summary  = $logs
                WHERE id = $id;
            ";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$status", (int)finalStatus);
            cmd.Parameters.AddWithValue("$finishedAt", DateTime.Now.ToString("O"));
            cmd.Parameters.AddWithValue("$err", (object?)errorMessage ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$logs", (object?)logsSummary ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendAssetPathsAsync(string id, IEnumerable<string> assetPaths, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));
        if (assetPaths == null) return;

        // Lấy list hiện tại trước, merge, rồi UPDATE — tránh race với row đã tồn tại.
        var merged = new List<string>();
        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using (var sel = conn.CreateCommand())
            {
                sel.CommandText = "SELECT asset_paths_json FROM task_runs WHERE id = $id;";
                sel.Parameters.AddWithValue("$id", id);
                var existing = (string?)await sel.ExecuteScalarAsync(ct);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(existing);
                        if (parsed != null) merged.AddRange(parsed);
                    }
                    catch
                    {
                        // Corrupt JSON — bỏ qua, ghi đè bằng list mới.
                    }
                }
            }

            foreach (var p in assetPaths)
            {
                if (!string.IsNullOrWhiteSpace(p) && !merged.Contains(p))
                {
                    merged.Add(p);
                }
            }

            await using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE task_runs SET asset_paths_json = $assets WHERE id = $id;";
            upd.Parameters.AddWithValue("$id", id);
            upd.Parameters.AddWithValue("$assets", JsonSerializer.Serialize(merged));
            await upd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<TaskRunHistoryEntry>> LoadAllAsync(CancellationToken ct = default)
    {
        var results = new List<TaskRunHistoryEntry>();
        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, task_type, project_name, flow_project_id, status,
                       started_at, finished_at, output_dir, asset_paths_json,
                       error_message, logs_summary, scriptwriter_gem_name, scene_creator_gem_name
                FROM task_runs
                ORDER BY started_at DESC;
            ";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var entry = new TaskRunHistoryEntry
                {
                    Id = reader.GetString(0),
                    TaskType = (HistoryTaskType)reader.GetInt32(1),
                    ProjectName = reader.GetString(2),
                    FlowProjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Status = (HistoryTaskStatus)reader.GetInt32(4),
                    StartedAt = DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    FinishedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    OutputDirectory = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                    LogsSummary = reader.IsDBNull(10) ? null : reader.GetString(10),
                    ScriptwriterGemName = reader.IsDBNull(11) ? null : reader.GetString(11),
                    SceneCreatorGemName = reader.IsDBNull(12) ? null : reader.GetString(12),
                };

                var assetsJson = reader.IsDBNull(8) ? null : reader.GetString(8);
                if (!string.IsNullOrWhiteSpace(assetsJson))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(assetsJson);
                        if (parsed != null) entry.AssetPaths = parsed;
                    }
                    catch
                    {
                        // Corrupt — để list rỗng.
                    }
                }

                results.Add(entry);
            }
        }
        finally
        {
            _gate.Release();
        }
        return results;
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM task_runs WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM task_runs;";
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void BindCommon(SqliteCommand cmd, TaskRunHistoryEntry entry)
    {
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$type", (int)entry.TaskType);
        cmd.Parameters.AddWithValue("$name", entry.ProjectName ?? string.Empty);
        cmd.Parameters.AddWithValue("$flowId", (object?)entry.FlowProjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$outputDir", (object?)entry.OutputDirectory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$assets",
            entry.AssetPaths != null && entry.AssetPaths.Count > 0
                ? JsonSerializer.Serialize(entry.AssetPaths)
                : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$logs", (object?)entry.LogsSummary ?? DBNull.Value);
    }
}
