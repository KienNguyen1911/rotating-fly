using AssetAutomator.Core.Models;

namespace AssetAutomator.Core.Interfaces
{
    public interface IConfigService
    {
        AppSettings CurrentSettings { get; }
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
        void EnsureDefaults(AppSettings settings);
    }
}
