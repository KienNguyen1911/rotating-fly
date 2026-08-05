using System;
using System.Collections.Generic;

namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Represents a Batch Image Generation project configuration and state.
    /// Saved as project.json inside Output/Projects/[ProjectName]/
    /// </summary>
    public class BatchProjectModel
    {
        public string ProjectId { get; set; } = Guid.NewGuid().ToString();
        public string ProjectName { get; set; } = "Default Project";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;

        public string ScriptJson { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public List<string> RefImagePaths { get; set; } = new List<string>();

        public string Provider { get; set; } = "flow_local";
        public string Engine { get; set; } = "flow";
        public string Model { get; set; } = "nano-banana-2";
        public string AspectRatio { get; set; } = "16:9";
        public string Upscale { get; set; } = "none";
        public int Concurrency { get; set; } = 4;
        public string? FlowProjectId { get; set; }
        public string? FlowProjectUrl { get; set; }

        public List<BatchImageItemState> Items { get; set; } = new List<BatchImageItemState>();

        public override string ToString() => ProjectName;
    }

    /// <summary>
    /// Serializable state for BatchImageItem inside project.json
    /// </summary>
    public class BatchImageItemState
    {
        public int Index { get; set; }
        public string SceneTitle { get; set; } = string.Empty;
        public string Transcript { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string Status { get; set; } = "Waiting";
        public string ImagePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string? MediaId { get; set; }
        public string? ReferenceMediaId { get; set; }
        public string? FlowProjectId { get; set; }
        public string? FlowProjectTitle { get; set; }
        public string? FlowProjectUrl { get; set; }
        public string Engine { get; set; } = "flow";
        public string Model { get; set; } = "nano-banana-2";
        public string AspectRatio { get; set; } = "16:9";
        public string Upscale { get; set; } = "none";
    }
}