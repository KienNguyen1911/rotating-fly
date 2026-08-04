using System;
using System.IO;
using System.Text.Json;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Infrastructure.Services
{
    public class ConfigService : IConfigService
    {
        private AppSettings _currentSettings = new AppSettings();
        private readonly string _appDataFolder;
        private readonly string _configPath;

        public ConfigService()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AssetAutomator");
            _configPath = Path.Combine(_appDataFolder, "appsettings.json");
        }

        public AppSettings CurrentSettings => _currentSettings;

        public AppSettings LoadSettings() => LoadSettingsImpl();

        public void SaveSettings(AppSettings settings) => SaveSettingsImpl(settings);

        public void EnsureDefaults(AppSettings settings) => EnsureDefaultsImpl(settings);

        private AppSettings LoadSettingsImpl()
        {
            var settings = new AppSettings();

            // Migrate local appsettings.json to AppData if local exists but AppData doesn't
            string localConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            if (File.Exists(localConfigPath) && !File.Exists(_configPath))
            {
                try
                {
                    if (!Directory.Exists(_appDataFolder))
                        Directory.CreateDirectory(_appDataFolder);
                    File.Copy(localConfigPath, _configPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigService] Error migrating local config: {ex.Message}");
                }
            }

            // Load from JSON config file in AppData
            if (File.Exists(_configPath))
            {
                try
                {
                    string json = File.ReadAllText(_configPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loaded != null)
                        settings = loaded;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigService] Error loading config: {ex.Message}");
                }
            }
            else
            {
                MigrateLegacyFiles(settings);
            }

            EnsureDefaultsImpl(settings);

            // Environment variable overrides
            string? envAi84Key = Environment.GetEnvironmentVariable("AI84_API_KEY");
            if (!string.IsNullOrEmpty(envAi84Key))
                settings.Ai84ApiKey = envAi84Key.Trim();

            string? envImageUrl = Environment.GetEnvironmentVariable("IMAGE_API_URL");
            if (!string.IsNullOrEmpty(envImageUrl))
                settings.ImageApiUrl = envImageUrl.Trim();

            string? envImageKey = Environment.GetEnvironmentVariable("IMAGE_API_KEY");
            if (!string.IsNullOrEmpty(envImageKey))
                settings.ImageApiKey = envImageKey.Trim();

            _currentSettings = settings;
            AutomationTask.BaseOutputsDirProvider = () => _currentSettings.OutputsDir;
            SaveSettingsImpl(settings);

            return settings;
        }

        private void EnsureDefaultsImpl(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ImageApiUrl))
                settings.ImageApiUrl = "http://127.0.0.1:8787/v1";

            if (string.IsNullOrWhiteSpace(settings.ImageApiKey))
                settings.ImageApiKey = "flow-local-key";

            if (string.IsNullOrWhiteSpace(settings.GoogleFlow2RootPath))
                settings.GoogleFlow2RootPath = Path.Combine(
                    AppContext.BaseDirectory, "tools", "PythonSource");

            if (settings.GoogleFlow2Port <= 0)
                settings.GoogleFlow2Port = 8787;

            if (string.IsNullOrWhiteSpace(settings.DefaultImageGenProvider))
                settings.DefaultImageGenProvider = "flow_local";

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
                    settings.ProxiesFilePath = localProxies;
            }

            if (string.IsNullOrWhiteSpace(settings.ManualProxiesFilePath) || (!File.Exists(settings.ManualProxiesFilePath) && settings.ManualProxiesFilePath.Contains(@"\Users\")))
            {
                settings.ManualProxiesFilePath = Path.Combine(_appDataFolder, "manual-proxies.txt");
            }

            if (string.IsNullOrWhiteSpace(settings.LicenseServerUrl))
                settings.LicenseServerUrl = "https://script.google.com/macros/s/AKfycbyE4Qhv2gxpPcMIYwL_yZAlRzglaHPSaxfHZarjDOeZ-WOSgqERaZWGBX_Gxrh58H2-/exec";

            if (string.IsNullOrWhiteSpace(settings.ProjectsStorageDir))
                settings.ProjectsStorageDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "Projects");
        }

        private void SaveSettingsImpl(AppSettings settings)
        {
            _currentSettings = settings;
            try
            {
                if (!Directory.Exists(_appDataFolder))
                    Directory.CreateDirectory(_appDataFolder);

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
                    settings.Ai84ApiKey = File.ReadAllText(ai84Path).Trim();

                string imgUrlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image_api_url.txt");
                if (File.Exists(imgUrlPath))
                    settings.ImageApiUrl = File.ReadAllText(imgUrlPath).Trim();

                string imgKeyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image_api_key.txt");
                if (File.Exists(imgKeyPath))
                    settings.ImageApiKey = File.ReadAllText(imgKeyPath).Trim();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Legacy migration failed: {ex.Message}");
            }
        }
    }
}
