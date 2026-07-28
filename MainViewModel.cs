using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AssetAutomator.Helpers;
using AssetAutomator.Models;
using AssetAutomator.Services;
using AssetAutomator.Windows;

namespace AssetAutomator
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private readonly IBrowserService _browserService;
        private readonly HistoryService _historyService;
        private readonly ImagePoolService _imagePoolService;
        private readonly ChatGptService _chatGptService;
        private readonly UpdateService _updateService;
        private readonly LicenseService _licenseService;
        private readonly IConfigService _configService;

        // Step services
        private readonly ThumbnailDownloadStep _step1;
        private readonly TranscriptExtractionStep _step2;
        private readonly ChatGptRewriteStep _step3;
        private readonly VoiceoverGenerationStep _step4;
        private readonly ImageGenerationStep _step5;
        private readonly LegacyVideoPipelineService _legacyVideoPipelineService;

        private bool _isDarkMode = false;

        public bool IsSrtMethod2Enabled => !string.IsNullOrWhiteSpace(_configService.CurrentSettings.SubtitleApiUrl);
        public Visibility IsSrtMethod2Visible => IsSrtMethod2Enabled ? Visibility.Visible : Visibility.Collapsed;

        public ObservableCollection<AutomationTask> Tasks { get; } = new();
        public ObservableCollection<string> ProfileList { get; } = new();
        public ObservableCollection<string> LanguageList { get; } = new(AppConstants.Languages);
        public ObservableCollection<string> HistoryDates { get; } = new();
        public ObservableCollection<HistoryTaskModel> HistoryTasks { get; } = new();

        public MainViewModel(
            IBrowserService browserService,
            HistoryService historyService,
            ImagePoolService imagePoolService,
            ChatGptService chatGptService,
            UpdateService updateService,
            LicenseService licenseService,
            IConfigService configService,
            ThumbnailDownloadStep step1,
            TranscriptExtractionStep step2,
            ChatGptRewriteStep step3,
            VoiceoverGenerationStep step4,
            ImageGenerationStep step5,
            LegacyVideoPipelineService legacyVideoPipelineService)
        {
            _browserService = browserService;
            _historyService = historyService;
            _imagePoolService = imagePoolService;
            _chatGptService = chatGptService;
            _updateService = updateService;
            _licenseService = licenseService;
            _configService = configService;
            _step1 = step1;
            _step2 = step2;
            _step3 = step3;
            _step4 = step4;
            _step5 = step5;
            _legacyVideoPipelineService = legacyVideoPipelineService;

            _imagePoolService.LogTask = LogTask;
            _imagePoolService.OnPoolStateChanged += UpdatePoolUi;

            _licenseService.OnSessionInvalidated += LicenseService_OnSessionInvalidated;

            LoadProfiles();
            LoadApplicationSettings();
            LoadHistoryDatesFromService();
        }

        private DateTime _lastUiUpdateTime = DateTime.MinValue;

        private void UpdatePoolUi()
        {
            var now = DateTime.Now;
            bool shouldUpdateDataGrid = false;
            if ((now - _lastUiUpdateTime).TotalMilliseconds >= 250)
            {
                shouldUpdateDataGrid = true;
                _lastUiUpdateTime = now;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var stats = _imagePoolService.GetPoolStats();
                // UI updates will be handled by the view via bindings
            }));
        }

        public void LoadApplicationSettings()
        {
            try
            {
                var settings = _configService.CurrentSettings;
                OnPropertyChanged(nameof(IsSrtMethod2Enabled));
                OnPropertyChanged(nameof(IsSrtMethod2Visible));
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load application settings: {ex.Message}");
            }
        }

        public void LoadHistoryDatesFromService()
        {
            try
            {
                HistoryDates.Clear();
                var dates = _historyService.LoadHistoryDates();
                foreach (var date in dates)
                {
                    HistoryDates.Add(date);
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load history dates: {ex.Message}");
            }
        }

        public void SaveApplicationSettings()
        {
            try
            {
                var settings = _configService.CurrentSettings;
                _configService.EnsureDefaults(settings);
                _configService.SaveSettings(settings);
                OnPropertyChanged(nameof(IsSrtMethod2Enabled));
                OnPropertyChanged(nameof(IsSrtMethod2Visible));
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to save application settings: {ex.Message}");
            }
        }

        public void LoadProfiles()
        {
            try
            {
                ProfileList.Clear();
                string baseDir = _configService.CurrentSettings.ChromeProfilesDir;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                {
                    var dirs = Directory.GetDirectories(baseDir);
                    foreach (var dir in dirs)
                    {
                        ProfileList.Add(Path.GetFileName(dir)!);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load profiles: {ex.Message}");
            }
        }

        public async Task<bool> CheckLicenseOnStartupAsync()
        {
            var resp = await _licenseService.VerifyAsync();
            if (!resp.Success)
            {
                Log($"[License] Startup check result: {resp.Message}");
                return false;
            }
            else
            {
                Log($"[License] Verification successful. Remaining days: {resp.RemainingDays}");
                return true;
            }
        }

        public async Task<bool> EnsureLicenseValidAsync()
        {
            var resp = await _licenseService.VerifyAsync();
            if (!resp.Success)
            {
                Log($"[LicenseGuard] Verification check failed: {resp.Message}");
                return false;
            }
            return true;
        }

        private void LicenseService_OnSessionInvalidated(object? sender, string reason)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                MessageBox.Show($"LICENSE WARNING:\n{reason}\n\nApplication will open License Management window.", "License Session Invalidated", MessageBoxButton.OK, MessageBoxImage.Warning);

                var win = new LicenseWindow(_licenseService) { Owner = Application.Current.MainWindow };
                win.ShowDialog();

                var recheck = await _licenseService.VerifyAsync();
                if (!recheck.Success)
                {
                    MessageBox.Show("Your license session has ended. Application will now close.", "Shutting Down", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                }
            });
        }

        public void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void LogTask(AutomationTask task, string message)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                task.Logs += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            }));
        }

        public void ToggleTheme()
        {
            _isDarkMode = !_isDarkMode;
            // Theme switching logic would be in the view
        }
    }
}