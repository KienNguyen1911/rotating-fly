using System.Collections.Generic;

namespace AssetAutomator.Core.Models
{
    public class SceneTimeModel
    {
        public string start { get; set; } = string.Empty;
        public string end { get; set; } = string.Empty;
        public double duration { get; set; }
    }

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

    public class ScenesJsonRootModel
    {
        public string video_title { get; set; } = string.Empty;
        public int scene_count { get; set; }
        public List<SceneJsonEntryModel> scenes { get; set; } = new();
    }

    public class SceneJsonEntryModel
    {
        public int scene { get; set; }
        public string id { get; set; } = string.Empty;
        public SceneTimeModel? time { get; set; }
        public string transcript { get; set; } = string.Empty;
        public string image_prompt { get; set; } = string.Empty;
    }
}
