using System;
using System.IO;
using System.Text.Json;
using AssetAutomator.Models;

namespace AssetAutomator.Services
{
    public class ConfigService : IConfigService
    {
        /// <summary>Static singleton instance, set automatically on construction.</summary>
        public static ConfigService? Instance { get; private set; }

        /// <summary>Static convenience accessor for CurrentSettings.</summary>
        public static AppSettings CurrentSettings => Instance?._currentSettings ?? new AppSettings();

        AppSettings IConfigService.CurrentSettings => _currentSettings;

        private AppSettings _currentSettings = new AppSettings();

        private readonly string _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetAutomator");
        private readonly string _configPath;

        public ConfigService()
        {
            Instance = this;
            _configPath = Path.Combine(_appDataFolder, "appsettings.json");
        }

        // ── Instance methods (IConfigService implementation) ──

        AppSettings IConfigService.LoadSettings() => LoadSettings();
        void IConfigService.SaveSettings(AppSettings s) => SaveSettings(s);
        void IConfigService.EnsureDefaults(AppSettings s) => EnsureDefaults(s);

        // ── Static convenience methods (for code that uses ConfigService statically) ──

        public static AppSettings LoadSettings() => Instance != null ? Instance.LoadSettingsImpl() : new AppSettings();
        public static void SaveSettings(AppSettings settings) => Instance?.SaveSettingsImpl(settings);
        public static void EnsureDefaults(AppSettings settings) => Instance?.EnsureDefaultsImpl(settings);

        // ── Private instance implementations ──

        private AppSettings LoadSettingsImpl()
        {
            var settings = new AppSettings();

            // 1. Migrate local appsettings.json to AppData if local exists but AppData doesn't
            string localConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(localConfigPath) && !File.Exists(_configPath))
            {
                try
                {
                    if (!Directory.Exists(_appDataFolder))
                    {
                        Directory.CreateDirectory(_appDataFolder);
                    }
                    File.Copy(localConfigPath, _configPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigService] Error migrating local config: {ex.Message}");
                }
            }

            // 2. Load from JSON config file in AppData if it exists
            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
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

            // 3. Ensure smart defaults for empty or invalid paths
            EnsureDefaultsImpl(settings);

            // 4. Override with Environment Variables if defined (Precedence: Env > Config File)
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

            _currentSettings = settings;

            // Auto-save to AppData to ensure any missing fields or defaults are persisted
            SaveSettingsImpl(settings);

            return settings;
        }

        private void EnsureDefaultsImpl(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ImageApiUrl))
                settings.ImageApiUrl = "http://localhost:8765";

            if (string.IsNullOrWhiteSpace(settings.ImageApiKey))
                settings.ImageApiKey = "chatgpt2api";

            if (string.IsNullOrWhiteSpace(settings.CustomGptUrl))
                settings.CustomGptUrl = "https://chatgpt.com/g/g-6a4083a0e37081919a248ef7721dae3d-dich-chay";

            if (string.IsNullOrWhiteSpace(settings.SubtitleApiUrl))
                settings.SubtitleApiUrl = "https://1834-34-28-139-249.ngrok-free.app/api/transcribe";

            if (settings.MaxConcurrentTasks <= 0)
                settings.MaxConcurrentTasks = 4;

            if (string.IsNullOrWhiteSpace(settings.ChromeProfilesDir))
                settings.ChromeProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");

            if (string.IsNullOrWhiteSpace(settings.OutputsDir))
                settings.OutputsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Outputs");

            if (string.IsNullOrWhiteSpace(settings.ProxiesFilePath) || (!File.Exists(settings.ProxiesFilePath) && settings.ProxiesFilePath.Contains(@"\Users\")))
            {
                string localProxies = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "free-proxies.json");
                if (File.Exists(localProxies))
                {
                    settings.ProxiesFilePath = localProxies;
                }
            }

            if (string.IsNullOrWhiteSpace(settings.ManualProxiesFilePath) || (!File.Exists(settings.ManualProxiesFilePath) && settings.ManualProxiesFilePath.Contains(@"\Users\")))
            {
                settings.ManualProxiesFilePath = Path.Combine(_appDataFolder, "manual-proxies.txt");
            }

            if (string.IsNullOrWhiteSpace(settings.LicenseServerUrl))
            {
                settings.LicenseServerUrl = "https://script.google.com/macros/s/AKfycbyE4Qhv2gxpPcMIYwL_yZAlRzglaHPSaxfHZarjDOeZ-WOSgqERaZWGBX_Gxrh58H2-/exec";
            }

            if (string.IsNullOrWhiteSpace(settings.ProjectsStorageDir))
            {
                settings.ProjectsStorageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Projects");
            }
        }

        private void SaveSettingsImpl(AppSettings settings)
        {
            _currentSettings = settings;
            try
            {
                if (!Directory.Exists(_appDataFolder))
                {
                    Directory.CreateDirectory(_appDataFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Error saving config: {ex.Message}");
            }
        }

        private void MigrateLegacyFiles(AppSettings settings)
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
