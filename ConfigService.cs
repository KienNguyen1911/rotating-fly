using System;
using System.IO;
using System.Text.Json;

namespace AutoCreateImage
{
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
    }

    public static class ConfigService
    {
        public static AppSettings CurrentSettings { get; set; } = new AppSettings();
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static AppSettings LoadSettings()
        {
            var settings = new AppSettings();

            // 1. Load from JSON config file if it exists
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                    {
                        settings = loaded;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigService] Error loading config: {ex.Message}");
                }
            }
            else
            {
                // Compatibility: Migrate from legacy text files if they exist
                MigrateLegacyFiles(settings);
            }

            // 2. Override with Environment Variables if defined (Precedence: Env > Config File)
            string? envAi84Key = Environment.GetEnvironmentVariable("AI84_API_KEY");
            if (!string.IsNullOrEmpty(envAi84Key))
            {
                settings.Ai84ApiKey = envAi84Key.Trim();
            }

            string? envImageUrl = Environment.GetEnvironmentVariable("IMAGE_API_URL");
            if (!string.IsNullOrEmpty(envImageUrl))
            {
                settings.ImageApiUrl = envImageUrl.Trim();
            }

            string? envImageKey = Environment.GetEnvironmentVariable("IMAGE_API_KEY");
            if (!string.IsNullOrEmpty(envImageKey))
            {
                settings.ImageApiKey = envImageKey.Trim();
            }

            CurrentSettings = settings;
            return settings;
        }

        public static void SaveSettings(AppSettings settings)
        {
            CurrentSettings = settings;
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
            }
        }

        private static void MigrateLegacyFiles(AppSettings settings)
        {
            try
            {
                string ai84Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai84_api_key.txt");
                if (File.Exists(ai84Path))
                {
                    settings.Ai84ApiKey = File.ReadAllText(ai84Path).Trim();
                }

                string imgUrlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image_api_url.txt");
                if (File.Exists(imgUrlPath))
                {
                    settings.ImageApiUrl = File.ReadAllText(imgUrlPath).Trim();
                }

                string imgKeyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image_api_key.txt");
                if (File.Exists(imgKeyPath))
                {
                    settings.ImageApiKey = File.ReadAllText(imgKeyPath).Trim();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Legacy migration failed: {ex.Message}");
            }
        }
    }
}
