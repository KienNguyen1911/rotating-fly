using System;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service managing automatic and manual application updates.
    /// UI-layer specific dialogs are handled by the presentation layer (WinUI).
    /// </summary>
    public class UpdateService
    {
        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/KienNguyen1911/AssetAutomator-Releases/main/update.xml";
        private readonly Action<string>? _logAction;
        private bool _isManualCheck;

        public UpdateService(Action<string>? logAction = null)
        {
            _logAction = logAction;
        }

        /// <summary>
        /// Initiates update check.
        /// </summary>
        /// <param name="isManualCheck">
        /// If true, force bypass CDN cache and enable error/status dialog reporting to the user.
        /// </param>
        public void CheckForUpdates(bool isManualCheck = false)
        {
            try
            {
                _isManualCheck = isManualCheck;

                string cacheBustedUrl = $"{UpdateXmlUrl}?t={DateTime.UtcNow.Ticks}";

                _logAction?.Invoke(isManualCheck
                    ? "[UpdateService] Checking for updates manually..."
                    : "[UpdateService] Checking for updates on startup...");
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[UpdateServiceError] Failed to check for updates: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets whether this check was initiated manually by the user.
        /// </summary>
        public bool IsManualCheck => _isManualCheck;

        /// <summary>
        /// Logs the up-to-date status. UI layer should show appropriate dialog if manual check.
        /// </summary>
        public void LogUpToDateStatus(string installedVersion)
        {
            _logAction?.Invoke($"[UpdateService] Application is up-to-date (v{installedVersion}).");
        }

        /// <summary>
        /// Logs an error message. UI layer should show appropriate dialog if manual check.
        /// </summary>
        public void LogError(string errorMessage)
        {
            _logAction?.Invoke($"[UpdateServiceError] Error checking for updates: {errorMessage}");
        }

        /// <summary>
        /// Logs update available status. UI layer should show update dialog.
        /// </summary>
        public void LogUpdateAvailable(string currentVersion, string installedVersion)
        {
            _logAction?.Invoke($"[UpdateService] Update available: v{currentVersion} (Installed: v{installedVersion})");
        }
    }
}
