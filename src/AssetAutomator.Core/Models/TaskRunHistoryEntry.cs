using System;
using System.Collections.Generic;

namespace AssetAutomator.Core.Models;

/// <summary>
/// Loại task được log vào lịch sử. Phân biệt rõ giữa full pipeline và các
/// step đơn lẻ để user có thể filter "chỉ xem Gemini writer" hay "chỉ xem
/// Batch Image Gen" mà không bị nhiễu bởi các lần chạy full pipeline.
/// </summary>
public enum HistoryTaskType
{
    /// <summary>Full pipeline (research → script → scenes → voiceover → image gen).</summary>
    FullPipeline = 0,

    /// <summary>Batch Image Gen (Google Flow Local hoặc legacy glabs).</summary>
    BatchImageGen = 1,

    /// <summary>Gemini topic research + script writing (Stage A trong orchestrator).</summary>
    GeminiWriter = 2,

    /// <summary>Gemini scene breakdown / scenes creator (Stage C trong orchestrator).</summary>
    GeminiScenesCreator = 3,

    /// <summary>Task chạy tay không qua pipeline (legacy / manual).</summary>
    Manual = 99,
}

/// <summary>
/// Trạng thái của một lần chạy. Mapping:
///   - <see cref="Running"/>: đang chạy, <see cref="FinishedAt"/> = null.
///   - <see cref="Success"/>/<see cref="Failed"/>/<see cref="Cancelled"/>: đã kết thúc, <see cref="FinishedAt"/> != null.
/// </summary>
public enum HistoryTaskStatus
{
    Running = 0,
    Success = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>
/// Một entry lịch sử cho một lần chạy task. Lưu trong SQLite table
/// <c>task_runs</c> (xem <c>SqliteTaskHistoryStore</c>).
///
/// Design notes:
///   - <see cref="Id"/> là GUID string để tránh clash giữa nhiều process
///     (batch cùng tên nhưng khác lần chạy).
///   - <see cref="AssetPaths"/> chỉ chứa path string; store dưới dạng
///     JSON array trong column <c>asset_paths_json</c>.
///   - <see cref="Duration"/> là computed property, không lưu xuống DB.
/// </summary>
public class TaskRunHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public HistoryTaskType TaskType { get; set; } = HistoryTaskType.Manual;

    /// <summary>Tên hiển thị của project / task (topic slug, project name, ...).</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Flow Local project id (nếu có), dùng để deep-link tới Flow dashboard.</summary>
    public string? FlowProjectId { get; set; }

    public HistoryTaskStatus Status { get; set; } = HistoryTaskStatus.Running;

    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>Null nếu task đang chạy; được set khi Status chuyển sang terminal state.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Computed: <c>FinishedAt - StartedAt</c>. Null nếu chưa kết thúc.</summary>
    public TimeSpan? Duration => FinishedAt.HasValue ? FinishedAt.Value - StartedAt : (TimeSpan?)null;

    /// <summary>Folder output tuyệt đối để mở Explorer / load scenes.json.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Danh sách đường dẫn assets (ảnh scenes, video, ...) để popup viewer.</summary>
    public List<string> AssetPaths { get; set; } = new();

    public string? ErrorMessage { get; set; }

    /// <summary>Tóm tắt logs cuối cùng (đã được format ngắn gọn).</summary>
    public string? LogsSummary { get; set; }

    /// <summary>
    /// Tên gem được dùng cho Stage A (research + script writing).
    /// Ví dụ: "Bedtime Writer", "Story Crafter Pro", "Default".
    /// Null cho các task không dùng Gemini (ví dụ Batch Image Gen).
    /// </summary>
    public string? ScriptwriterGemName { get; set; }

    /// <summary>
    /// Tên gem được dùng cho Stage C (scene breakdown).
    /// Ví dụ: "Creative Storyteller", "Default".
    /// Null nếu không chạy stage C hoặc task không dùng Gemini.
    /// </summary>
    public string? SceneCreatorGemName { get; set; }

    /// <summary>
    /// Pretty-formatted gems summary cho UI binding, ví dụ:
    ///   "✍️ Bedtime Writer → 🎬 Creative Storyteller"
    ///   "✍️ Story Crafter Pro"
    ///   "—" nếu cả 2 đều null
    /// </summary>
    public string GemsSummary
    {
        get
        {
            bool hasScript = !string.IsNullOrWhiteSpace(ScriptwriterGemName);
            bool hasScene = !string.IsNullOrWhiteSpace(SceneCreatorGemName);

            if (!hasScript && !hasScene) return "—";

            string scriptPart = hasScript ? $"✍️ {ScriptwriterGemName}" : "";
            string scenePart = hasScene ? $"🎬 {SceneCreatorGemName}" : "";

            if (hasScript && hasScene) return $"{scriptPart} → {scenePart}";
            return scriptPart + scenePart;
        }
    }

    /// <summary>
    /// Background hex color cho status pill (tinted, dùng cho XAML binding).
    /// Format "#AARRGGBB" (alpha-first để dùng trực tiếp SolidColorBrush).
    /// </summary>
    public string StatusBackgroundHex => Status switch
    {
        HistoryTaskStatus.Running    => "#1F0078D4",
        HistoryTaskStatus.Success   => "#1F107C06",
        HistoryTaskStatus.Failed   => "#1FC42F1C",
        HistoryTaskStatus.Cancelled => "#1F797979",
        _ => "#1F797979",
    };

    /// <summary>
    /// Foreground hex color cho status text (solid color, dùng cho XAML binding).
    /// </summary>
    public string StatusForegroundHex => Status switch
    {
        HistoryTaskStatus.Running    => "#FF0078D4",
        HistoryTaskStatus.Success   => "#FF107C06",
        HistoryTaskStatus.Failed   => "#FFC42F1C",
        HistoryTaskStatus.Cancelled => "#FF797979",
        _ => "#FF797979",
    };

    /// <summary>Pretty-formatted duration cho UI binding, e.g. "00:01:23".</summary>
    public string DurationFormatted => Duration.HasValue
        ? Duration.Value.ToString(@"hh\:mm\:ss")
        : "—";

    /// <summary>Pretty-formatted start timestamp cho UI binding.</summary>
    public string StartedAtFormatted => StartedAt.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Display name cho task type, dùng trong UI badge.</summary>
    public string TaskTypeDisplayName => TaskType switch
    {
        HistoryTaskType.FullPipeline => "🚀 Full Pipeline",
        HistoryTaskType.BatchImageGen => "🖼️ Batch Image Gen",
        HistoryTaskType.GeminiWriter => "✍️ Gemini Writer",
        HistoryTaskType.GeminiScenesCreator => "🎬 Gemini Scenes",
        HistoryTaskType.Manual => "📝 Manual",
        _ => TaskType.ToString(),
    };

    /// <summary>Display name cho status, dùng trong UI badge.</summary>
    public string StatusDisplayName => Status switch
    {
        HistoryTaskStatus.Running => "⏳ Running",
        HistoryTaskStatus.Success => "✅ Success",
        HistoryTaskStatus.Failed => "❌ Failed",
        HistoryTaskStatus.Cancelled => "⏹️ Cancelled",
        _ => Status.ToString(),
    };
}
