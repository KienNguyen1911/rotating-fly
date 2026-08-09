namespace AssetAutomator.Core.Models
{
    /// <summary>
    /// Application configuration model. Serialized to/from appsettings.json.
    /// Extracted from ConfigService.cs for clean model separation.
    /// </summary>
    public class AppSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Ai84ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Default Google Flow Local API base URL.
        /// The launcher in <c>D:\Dev\google-flow-2.0.0</c> serves
        /// OpenAI-compatible endpoints on port 8787 by default.
        /// </summary>
        public string ImageApiUrl { get; set; } = "http://127.0.0.1:8787/v1";

        /// <summary>
        /// Default API key expected by the Google Flow Local launcher
        /// (any non-empty value is accepted; the launcher only uses this
        /// to authenticate internal calls, not Google).
        /// </summary>
        public string ImageApiKey { get; set; } = "flow-local-key";

        /// <summary>
        /// Absolute path to the directory that hosts <c>google_flow/</c>
        /// and <c>google_flow_ext/</c> source trees — used as the
        /// Python process' <c>WorkingDirectory</c>. Defaults to the
        /// bundled <c>tools/PythonSource/</c> next to the WinUI exe so
        /// end-users never need to install Python or clone the repo.
        /// </summary>
        public string GoogleFlow2RootPath { get; set; } = Path.Combine(
            AppContext.BaseDirectory, "tools", "PythonSource");

        /// <summary>
        /// TCP port the google-flow-2.0.0 launcher binds to.
        /// Must match <see cref="ImageApiUrl"/>'s port.
        /// </summary>
        public int GoogleFlow2Port { get; set; } = 8787;

        /// <summary>
        /// Set to <c>false</c> to disable the auto-launch behaviour
        /// (e.g. when running the server externally in dev mode).
        /// </summary>
        public bool GoogleFlow2AutoLaunch { get; set; } = true;

        public string SupabaseDbUrl { get; set; } = string.Empty;
        public string ChromeProfilesDir { get; set; } = string.Empty;
        public string OutputsDir { get; set; } = string.Empty;
        public int MaxConcurrentTasks { get; set; } = 4;
        public string DefaultChromeProfile { get; set; } = string.Empty;
        public string CustomGptUrl { get; set; } = "https://chatgpt.com/g/g-6a4083a0e37081919a248ef7721dae3d-dich-chay";
        public string SubtitleApiUrl { get; set; } = "https://1834-34-28-139-249.ngrok-free.app/api/transcribe";
        public string ProxiesFilePath { get; set; } = string.Empty;
        public string ManualProxiesFilePath { get; set; } = string.Empty;
        public string LicenseServerUrl { get; set; } = "https://script.google.com/macros/s/AKfycbyE4Qhv2gxpPcMIywL_yZAlRzglaHPSaxfHZarjDOeZ-WOSgqERaZWGBX_Gxrh58H2-/exec";
        public string LicenseKey { get; set; } = string.Empty;
        public string ProjectsStorageDir { get; set; } = string.Empty;
        public string GeminiApiBaseUrl { get; set; } = "http://localhost:8000";
        public string ScriptwriterGemId { get; set; } = string.Empty;
        public string SceneCreatorGemId { get; set; } = string.Empty;

        /// <summary>
        /// Always <c>flow_local</c>. Kept for backward-compat with existing
        /// <c>appsettings.json</c> files; new code treats this as a constant.
        /// </summary>
        public string DefaultImageGenProvider { get; set; } = "flow_local";

        /// <summary>
        /// Master toggle for the Gemini watermark-removal pipeline. When
        /// <c>false</c>, the gwr CLI is never invoked and images are saved
        /// with the Gemini watermark intact. Default <c>true</c> per product
        /// requirement (user explicitly opted out via settings UI).
        /// </summary>
        public bool EnableWatermarkRemoval { get; set; } = true;

        /// <summary>
        /// Max concurrent watermark-removal subprocesses. Bounded 1..8.
        /// Falls back to <c>max(2, ProcessorCount / 2)</c> when 0.
        /// </summary>
        public int WatermarkMaxParallel { get; set; } = 0;

        // ─────────────────────────────────────────────────────
        //  wiltodelta/remove-ai-watermarks (Python) — v2.1.0+
        //  Switched from @pilio/gemini-watermark-remover (math-only,
        //  catalog-bound, brittle) to remove-ai-watermarks (inpainting
        //  via OpenCV/MI-GAN/LaMa, robust to non-standard layouts).
        //
        //  The Python runtime is the embedded one created by
        //  tools/Scripts/Setup-PythonEmbed.ps1 (Python 3.11.9 at
        //  tools/PythonEmbed/python.exe). The package
        //  `remove-ai-watermarks[visible]` is installed into the
        //  embedded venv during that same setup.
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Inpainting backend for the <c>visible</c> subcommand:
        /// <c>auto</c> (CLI picks best installed: LaMa > MI-GAN > cv2),
        /// <c>cv2</c> (classical OpenCV Telea, fast, no model download),
        /// <c>migan</c> (MI-GAN ONNX, ~1GB, texture-aware), or
        /// <c>lama</c> (LaMa ONNX, ~4.7GB, best quality). The MI-GAN/LaMa
        /// options require the matching <c>remove-ai-watermarks[backend]</c>
        /// extra to be installed in the embedded Python.
        /// </summary>
        public string WatermarkInpaintBackend { get; set; } = "auto";

        /// <summary>
        /// Per-image timeout in seconds. The default (30s) is generous for
        /// cv2 inpainting on 4K images. MI-GAN/LaMa are slower and may
        /// need 60-120s for large images.
        /// </summary>
        public int WatermarkPerImageTimeoutSec { get; set; } = 30;
    }
}