namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Application configuration model. Serialized to/from appsettings.json.
    /// Extracted from ConfigService.cs for clean model separation.
    /// </summary>
    public class AppSettings
    {
        public string Ai84ApiKey { get; set; } = string.Empty;
        public string ImageApiUrl { get; set; } = "http://localhost:8765";
        public string ImageApiKey { get; set; } = "chatgpt2api";
        public string SupabaseDbUrl { get; set; } = string.Empty;
        public string ChromeProfilesDir { get; set; } = string.Empty;
        public string OutputsDir { get; set; } = string.Empty;
        public int MaxConcurrentTasks { get; set; } = 4;
        public string DefaultChromeProfile { get; set; } = string.Empty;
        public string CustomGptUrl { get; set; } = "https://chatgpt.com/g/g-6a4083a0e37081919a248ef7721dae3d-dich-chay";
        public string SubtitleApiUrl { get; set; } = "https://1834-34-28-139-249.ngrok-free.app/api/transcribe";
        public string ProxiesFilePath { get; set; } = string.Empty;
        public string ManualProxiesFilePath { get; set; } = string.Empty;
        public string LicenseServerUrl { get; set; } = "https://script.google.com/macros/s/AKfycbyE4Qhv2gxpPcMIYwL_yZAlRzglaHPSaxfHZarjDOeZ-WOSgqERaZWGBX_Gxrh58H2-/exec";
        public string LicenseKey { get; set; } = string.Empty;
        public string ProjectsStorageDir { get; set; } = string.Empty;
        public string GeminiApiBaseUrl { get; set; } = "http://localhost:8000";
        public string ScriptwriterGemId { get; set; } = string.Empty;
        public string SceneCreatorGemId { get; set; } = string.Empty;
        public string DefaultImageGenProvider { get; set; } = "flow_local";
    }
}
