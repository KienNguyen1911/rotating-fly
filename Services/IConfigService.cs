using AssetAutomator.Models;

namespace AssetAutomator.Services
{
    public interface IConfigService
    {
        AppSettings CurrentSettings { get; }
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
        void EnsureDefaults(AppSettings settings);
    }
}