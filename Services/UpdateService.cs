using System;
using System.Windows;
using AutoUpdaterDotNET;
using AssetAutomator.Windows;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Service managing automatic and manual application updates via AutoUpdater.NET with custom WPF dialog UI.
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

                AutoUpdater.CheckForUpdateEvent -= AutoUpdater_CheckForUpdateEvent;
                AutoUpdater.CheckForUpdateEvent += AutoUpdater_CheckForUpdateEvent;

                AutoUpdater.ReportErrors = isManualCheck;
                AutoUpdater.Synchronous = false;

                // Append timestamp to query string to bypass Fastly CDN / HTTP caching on GitHub raw files
                string cacheBustedUrl = $"{UpdateXmlUrl}?t={DateTime.UtcNow.Ticks}";

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

        private void AutoUpdater_CheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            AutoUpdater.CheckForUpdateEvent -= AutoUpdater_CheckForUpdateEvent;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (args.Error != null)
                {
                    _logAction?.Invoke($"[UpdateServiceError] Error checking for updates: {args.Error.Message}");
                    if (_isManualCheck)
                    {
                        MessageBox.Show(
                            $"Không thể kiểm tra bản cập nhật mới.\n\nChi tiết lỗi: {args.Error.Message}",
                            "Lỗi Kiểm Tra Cập Nhật",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return;
                }

                if (args.IsUpdateAvailable)
                {
                    _logAction?.Invoke($"[UpdateService] Update available: v{args.CurrentVersion} (Installed: v{args.InstalledVersion})");
                    var updateWin = new UpdateWindow(args);
                    if (Application.Current?.MainWindow != null && Application.Current.MainWindow.IsVisible)
                    {
                        updateWin.Owner = Application.Current.MainWindow;
                    }
                    updateWin.ShowDialog();
                }
                else
                {
                    _logAction?.Invoke($"[UpdateService] Application is up-to-date (v{args.InstalledVersion}).");
                    if (_isManualCheck)
                    {
                        MessageBox.Show(
                            $"Bạn đang sử dụng phiên bản mới nhất ({args.InstalledVersion})!",
                            "Kiểm Tra Cập Nhật",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
            });
        }
    }
}
