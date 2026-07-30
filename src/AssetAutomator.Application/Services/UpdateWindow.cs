using System;
using System.Windows;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Placeholder for the auto-updater dialog window. The full WPF UpdateWindow UI
    /// (with progress bar + changelog) lives in AssetAutomator.UI (to be migrated).
    /// </summary>
    public class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            Title = "Update Available";
            Width = 500;
            Height = 300;
        }
    }
}