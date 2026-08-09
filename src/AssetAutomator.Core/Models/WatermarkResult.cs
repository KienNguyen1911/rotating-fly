using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Result of a single <see cref="Interfaces.IWatermarkRemover.RemoveAsync"/>
    /// call. Immutable on purpose so it can be safely cached and shared across
    /// UI threads without locking.
    /// </summary>
    /// <remarks>
    /// Lives in <c>Core</c> rather than <c>Application</c> because the
    /// <c>IWatermarkRemover</c> interface (also in <c>Core</c>) needs to
    /// reference it; a Core interface cannot depend on a higher layer.
    /// </remarks>
    public sealed class WatermarkResult
    {
        /// <summary>
        /// True if the file was overwritten with a clean version (no Gemini watermark).
        /// False if the operation was skipped or failed — in that case the file on
        /// disk is UNCHANGED (still watermarked).
        /// </summary>
        public bool Applied { get; init; }

        /// <summary>
        /// Confidence tier reported by the gwr CLI:
        ///   - <c>exact</c>: catalog hit + 1:1 alpha-map restore (pixel-perfect).
        ///   - <c>best-effort</c>: detection found a watermark but fit was not certain.
        ///   - <c>none</c>: no watermark detected (image is likely already clean).
        /// </summary>
        public string DecisionTier { get; init; } = "none";

        /// <summary>
        /// How long the CLI took to process this image, in milliseconds.
        /// Useful for the HistoryPage tooltip and for capacity planning.
        /// </summary>
        public long DurationMs { get; init; }

        /// <summary>
        /// Reason the operation was skipped / failed. Human-readable English,
        /// suitable for the HistoryPage secondary column. Empty when
        /// <see cref="Applied"/> = true.
        /// </summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>
        /// Convenience factory for the success path.
        /// </summary>
        public static WatermarkResult Clean(string tier, long durationMs) =>
            new() { Applied = true, DecisionTier = tier, DurationMs = durationMs, Reason = string.Empty };

        /// <summary>
        /// Convenience factory for the failure paths. The file on disk is unchanged.
        /// </summary>
        public static WatermarkResult Skipped(string reason) =>
            new() { Applied = false, DecisionTier = "none", DurationMs = 0, Reason = reason };

        /// <summary>
        /// Maps the result to a short badge text for the UI:
        ///   ✨ Clean (exact) | ✨ Clean (best-effort) | 💧 Watermarked | ─
        /// </summary>
        [JsonIgnore]
        public string BadgeText => Applied
            ? (DecisionTier == "exact" ? "✨ Clean" : "✨ Clean*")
            : "💧 Watermarked";

        /// <summary>
        /// Tooltip text for the badge — full reason on hover.
        /// </summary>
        [JsonIgnore]
        public string BadgeTooltip => Applied
            ? $"Watermark removed ({DecisionTier}, {DurationMs}ms)"
            : string.IsNullOrWhiteSpace(Reason)
                ? "Gemini watermark detected but not removed"
                : $"Watermark not removed: {Reason}";
    }

    /// <summary>
    /// Internal DTO for parsing the gwr CLI's <c>--json</c> output. Mirrors
    /// <c>bin/gwr.mjs</c>'s stdout schema:
    /// <code>
    /// {
    ///   "ok": true|false,
    ///   "meta": {
    ///     "applied": true|false,
    ///     "skipReason": "..." | null
    ///   },
    ///   "decisionTier": "exact" | "best-effort" | "none",
    ///   "durationMs": 42,
    ///   "input": "...",
    ///   "output": "..."
    /// }
    /// </code>
    /// Important: the modern CLI puts <c>applied</c> and <c>skipReason</c>
    /// inside <c>meta</c>, NOT at the top level. Older revisions had them at
    /// the top level — we prefer the new shape but the top-level fields are
    /// kept for forward compatibility (filled by parser if meta is absent).
    /// </summary>
    public sealed class GwrCliJsonOutput
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }

        // ── Newer CLI shape: applied + reason live inside `meta`. ──
        [JsonPropertyName("meta")] public GwrMeta? Meta { get; set; }

        [JsonPropertyName("decisionTier")] public string DecisionTier { get; set; } = "none";
        [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("input")] public string? Input { get; set; }
        [JsonPropertyName("output")] public string? Output { get; set; }

        /// <summary>
        /// Convenience: read <c>applied</c> preferring <c>meta.applied</c>;
        /// fall back to top-level <c>applied</c> for older CLI versions that
        /// haven't moved the flag inside <c>meta</c> yet.
        /// </summary>
        [JsonIgnore]
        public bool Applied =>
            Meta?.Applied ?? false;

        /// <summary>
        /// Convenience: read the skip-reason preferring <c>meta.skipReason</c>;
        /// fall back to top-level <c>reason</c>.
        /// </summary>
        [JsonIgnore]
        public string? EffectiveReason =>
            Meta?.SkipReason ?? Reason;
    }

    /// <summary>
    /// The <c>meta</c> sub-object in the gwr CLI JSON output. Houses the
    /// actual application result + skip reason + sizing details.
    /// </summary>
    public sealed class GwrMeta
    {
        [JsonPropertyName("applied")] public bool Applied { get; set; }
        [JsonPropertyName("skipReason")] public string? SkipReason { get; set; }
        [JsonPropertyName("size")] public int? Size { get; set; }
    }
}
