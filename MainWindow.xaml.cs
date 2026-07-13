using System;
using System.IO;
using System.Windows;
using System.Collections.ObjectModel;
using Microsoft.Playwright;

namespace AutoCreateImage
{
    public partial class MainWindow : Window
    {
        private IPlaywright? _playwright;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IBrowserContext> _browserContexts = new System.Collections.Concurrent.ConcurrentDictionary<string, IBrowserContext>();
        private readonly System.Threading.SemaphoreSlim _clipboardSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private readonly System.Threading.SemaphoreSlim _browserInitSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        
        public ObservableCollection<AutomationTask> Tasks { get; set; } = new ObservableCollection<AutomationTask>();
        public ObservableCollection<string> ProfileList { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> LanguageList { get; set; } = new ObservableCollection<string>(AppConstants.Languages);
        public ObservableCollection<string> HistoryDates { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<HistoryTaskModel> HistoryTasks { get; set; } = new ObservableCollection<HistoryTaskModel>();

        public MainWindow()
        {
            InitializeComponent();
            DgridTasks.ItemsSource = Tasks;
            DataContext = this;
            Log("Application started. Ready to run tasks.");
            LoadProfiles();
            LoadApplicationSettings();
            LoadHistoryDates();
        }

        private void LoadApplicationSettings()
        {
            try
            {
                var settings = ConfigService.LoadSettings();
                TxtAi84ApiKey.Text = settings.Ai84ApiKey;
                TxtImageApiUrl.Text = settings.ImageApiUrl;
                TxtImageApiKey.Text = settings.ImageApiKey;
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load application settings: {ex.Message}");
            }
        }

        private void SaveApplicationSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    Ai84ApiKey = TxtAi84ApiKey.Text.Trim(),
                    ImageApiUrl = TxtImageApiUrl.Text.Trim(),
                    ImageApiKey = TxtImageApiKey.Text.Trim()
                };
                ConfigService.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to save application settings: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}