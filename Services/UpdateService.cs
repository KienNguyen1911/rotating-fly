using System;
using AutoUpdaterDotNET;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Service managing automatic and manual application updates via AutoUpdater.NET.
    /// </summary>
    public class UpdateService
    {
        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/KienNguyen1911/AssetAutomator-Releases/main/update.xml";
        private readonly Action<string>? _logAction;

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
                // Append timestamp to query string to bypass Fastly CDN / HTTP caching on GitHub raw files
                string cacheBustedUrl = $"{UpdateXmlUrl}?t={DateTime.UtcNow.Ticks}";

                AutoUpdater.ReportErrors = isManualCheck;
                AutoUpdater.Synchronous = false;

                _logAction?.Invoke(isManualCheck
                    ? "[UpdateService] Checking for updates manually..."
                    : "[UpdateService] Checking for updates on startup...");

                AutoUpdater.Start(cacheBustedUrl);
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[UpdateServiceError] Failed to check for updates: {ex.Message}");
            }
        }
    }
}
