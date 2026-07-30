using System;

namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Represents the lifecycle status of a pipeline node.
    /// Used across the application layer for tracking task progress.
    /// </summary>
    public enum NodeStatus
    {
        Idle,
        Running,
        Success,
        Failed
    }

    /// <summary>
    /// Represents a Gemini Gem option for UI binding (no WPF deps).
    /// </summary>
    public class GemOptionItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;

        // Value equality by Id so ComboBox-like controls preserve SelectedItem
        // even after the collection is cleared and re-populated with new instances.
        public override bool Equals(object? obj) =>
            obj is GemOptionItem other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            Id?.ToLowerInvariant().GetHashCode() ?? 0;
    }
}