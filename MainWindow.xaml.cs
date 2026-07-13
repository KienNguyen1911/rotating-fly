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

        private readonly ApiServerManager _apiManager = new ApiServerManager();

        public MainWindow()
        {
            InitializeComponent();
            DgridTasks.ItemsSource = Tasks;
            DataContext = this;
            Log("Application started. Ready to run tasks.");
            LoadProfiles();
            LoadApplicationSettings();
            LoadHistoryDates();

            // Set up API Server Management
            _apiManager.LogReceived += ApiManager_LogReceived;
            _apiManager.StatusChanged += ApiManager_StatusChanged;
            Closing += MainWindow_Closing;

            // Start API Server automatically on startup
            _apiManager.StartServer();
        }

        private void ApiManager_LogReceived(string log)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                TxtApiLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {log}\n");
                if (TxtApiLogs.Text.Length > 30000)
                {
                    TxtApiLogs.Text = TxtApiLogs.Text.Substring(15000);
                }
                TxtApiLogs.ScrollToEnd();
            }));
        }

        private void ApiManager_StatusChanged()
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                bool isRunning = _apiManager.IsRunning;
                TxtApiServerStatus.Text = isRunning ? "Đang chạy" : "Đã dừng";
                
                var color = isRunning ? System.Windows.Media.Colors.LightGreen : System.Windows.Media.Colors.Tomato;
                var brush = new System.Windows.Media.SolidColorBrush(color);
                
                TxtApiServerStatus.Foreground = brush;
                ApiStatusIndicator.Fill = brush;

                if (isRunning)
                {
                    _ = Task.Run(async () => await RefreshAccountsListAsync());
                }
            }));
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveApplicationSettings();
            _apiManager.StopServer();
        }

        private async Task RefreshAccountsListAsync()
        {
            if (!_apiManager.IsRunning) return;
            var accounts = await _apiManager.GetAccountsAsync();
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                DgridAccounts.ItemsSource = accounts;
            }));
        }

        private void BtnStartApi_Click(object sender, RoutedEventArgs e)
        {
            if (!_apiManager.IsRunning)
            {
                _apiManager.StartServer();
            }
        }

        private void BtnStopApi_Click(object sender, RoutedEventArgs e)
        {
            if (_apiManager.IsRunning)
            {
                _apiManager.StopServer();
            }
        }

        private async void BtnRefreshAccounts_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAccountsListAsync();
        }

        private async void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            if (!_apiManager.IsRunning)
            {
                MessageBox.Show("Vui lòng khởi động API Server trước.", "Server chưa chạy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string emailHint = TxtEmailHint.Text.Trim();
            var startResult = await _apiManager.StartOAuthLoginAsync(emailHint);
            if (startResult == null)
            {
                MessageBox.Show("Không thể bắt đầu phiên đăng nhập OAuth. Kiểm tra log API để biết thêm chi tiết.", "Lỗi OAuth", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var loginWin = new WebViewLoginWindow(startResult.SessionId, startResult.AuthorizeUrl, startResult.RedirectUriPrefix)
            {
                Owner = this
            };

            if (loginWin.ShowDialog() == true)
            {
                string callbackUrl = loginWin.CallbackUrl;
                bool success = await _apiManager.FinishOAuthLoginAsync(startResult.SessionId, callbackUrl);
                if (success)
                {
                    MessageBox.Show("Đăng nhập và tích hợp tài khoản ChatGPT thành công!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    await RefreshAccountsListAsync();
                }
                else
                {
                    MessageBox.Show("Có lỗi xảy ra khi đồng bộ Token ChatGPT từ Callback URL.", "Lỗi đồng bộ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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