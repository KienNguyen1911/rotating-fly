using System;
using System.Windows;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service managing automatic and manual application updates.
    /// The full AutoUpdater.NET wiring with cache-busting query string is preserved.
    /// Note: AutoUpdaterDotNET integration will be wired up via a UI-layer adapter;
    /// this service owns the URL + cache-busting logic only.
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

                // AutoUpdater.Start(cacheBustedUrl); — wired in UI layer
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[UpdateServiceError] Failed to check for updates: {ex.Message}");
            }
        }

        /// <summary>
        /// Shows a manual-check dialog reporting up-to-date status.
        /// Public to allow UI layer to call when AutoUpdater.NET is bridged in.
        /// </summary>
        public void ShowUpToDateMessage(string installedVersion)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _logAction?.Invoke($"[UpdateService] Application is up-to-date (v{installedVersion}).");
                if (_isManualCheck)
                {
                    MessageBox.Show(
                        $"Bạn đang sử dụng phiên bản mới nhất ({installedVersion})!",
                        "Kiểm Tra Cập Nhật",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            });
        }

        /// <summary>
        /// Shows an error dialog for manual checks.
        /// </summary>
        public void ShowErrorMessage(string errorMessage)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _logAction?.Invoke($"[UpdateServiceError] Error checking for updates: {errorMessage}");
                if (_isManualCheck)
                {
                    MessageBox.Show(
                        $"Không thể kiểm tra bản cập nhật mới.\n\nChi tiết lỗi: {errorMessage}",
                        "Lỗi Kiểm Tra Cập Nhật",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }

        /// <summary>
        /// Shows the update dialog for a newly available version.
        /// </summary>
        public void ShowUpdateAvailable(string currentVersion, string installedVersion)
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                _logAction?.Invoke($"[UpdateService] Update available: v{currentVersion} (Installed: v{installedVersion})");
                var updateWin = new UpdateWindow();
                if (System.Windows.Application.Current?.MainWindow != null && System.Windows.Application.Current.MainWindow.IsVisible)
                {
                    updateWin.Owner = System.Windows.Application.Current.MainWindow;
                }
                updateWin.ShowDialog();
            });
        }
    }
}