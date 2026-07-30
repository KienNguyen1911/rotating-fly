using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.UI
{
    /// <summary>
    /// Static convenience accessor for the registered <see cref="IConfigService"/>.
    /// Maintains backward compatibility with legacy code paths that referenced
    /// <c>ConfigService.CurrentSettings</c> directly. Initialized once during
    /// <see cref="App"/> startup before any window is shown.
    /// </summary>
    public static class ConfigService
    {
        public static IConfigService Instance { get; private set; } = null!;

        /// <summary>Bound at App startup to the singleton <see cref="IConfigService"/>.</summary>
        public static AppSettings CurrentSettings =>
            Instance?.CurrentSettings ?? new AppSettings();

        public static AppSettings LoadSettings() =>
            Instance?.LoadSettings() ?? new AppSettings();

        public static void SaveSettings(AppSettings settings) =>
            Instance?.SaveSettings(settings);

        public static void EnsureDefaults(AppSettings settings) =>
            Instance?.EnsureDefaults(settings);

        public static void SetProvider(IConfigService provider)
        {
            Instance = provider;
        }
    }
}
