using System.Collections.Generic;

namespace AssetAutomator.Core.Models
{
    public class SceneTimeModel
    {
        public string start { get; set; } = string.Empty;
        public string end { get; set; } = string.Empty;
        public double duration { get; set; }
    }

    /// <summary>
    /// Model representing an individual scene entry parsed from scenes.json.
    /// </summary>
    public class SceneItemModel
    {
        public int SceneNumber { get; set; }
        public string Id { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public double Duration { get; set; }
        public string Transcript { get; set; } = string.Empty;
        public string ImagePrompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Model representing the root structure of scenes.json.
    /// </summary>
    public class ScenesJsonRootModel
    {
        public string video_title { get; set; } = string.Empty;
        public int scene_count { get; set; }
        /// <summary>
        /// v4.0 DRY: Style signature + negative prompt + aspect ratio.
        /// Dùng chung cho mọi scene. Ghép vào mỗi scene khi generate final prompt.
        /// </summary>
        public string? image_prompt_postfix { get; set; }
        public List<SceneJsonEntryModel> scenes { get; set; } = new List<SceneJsonEntryModel>();
    }

    public class SceneJsonEntryModel
    {
        public int scene { get; set; }
        public string id { get; set; } = string.Empty;
        public SceneTimeModel? time { get; set; }
        public string transcript { get; set; } = string.Empty;
        public string image_prompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// v4.0 DRY format response từ Image Prompt Gemini.
    /// image_prompt_postfix chứa style signature + negative prompt + aspect ratio (dùng chung cho mọi scene).
    /// scenes dict chỉ chứa content riêng cho từng scene (KHÔNG có postfix).
    /// </summary>
    public class ImagePromptResponseModel
    {
        public string image_prompt_postfix { get; set; } = string.Empty;
        public Dictionary<string, string> scenes { get; set; } = new();
    }

    /// <summary>
    /// Model cho scenes_raw.json - chỉ chứa scene segmentation (không có image_prompt).
    /// Output từ Stage C: Scene Segmentation.
    /// </summary>
    public class SceneSegmentationModel
    {
        public string video_title { get; set; } = string.Empty;
        public int scene_count { get; set; }
        public List<SceneSegmentModel> scenes { get; set; } = new();
    }

    /// <summary>
    /// Model cho từng scene trong scenes_raw.json.
    /// KHÔNG có image_prompt - field này được thêm ở Stage D.
    /// Output từ scenes-splitter.md gem: {"scene": 1, "id": "scene_001", ...}
    /// </summary>
    public class SceneSegmentModel
    {
        public int scene { get; set; }
        public string id { get; set; } = string.Empty;
        public SceneTimeModel? time { get; set; }
        public string transcript { get; set; } = string.Empty;
    }
}