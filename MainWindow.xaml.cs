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
                PbSettingsAi84ApiKey.Password = settings.Ai84ApiKey;
                PbSettingsSupabaseDbUrl.Password = settings.SupabaseDbUrl;
                TxtSettingsChromeProfilesDir.Text = settings.ChromeProfilesDir;
                TxtSettingsOutputsDir.Text = settings.OutputsDir;
                TxtSettingsMaxConcurrentTasks.Text = settings.MaxConcurrentTasks.ToString();
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
                int.TryParse(TxtSettingsMaxConcurrentTasks.Text.Trim(), out int maxTasks);
                if (maxTasks <= 0) maxTasks = 4;

                var settings = ConfigService.CurrentSettings;
                settings.Ai84ApiKey = PbSettingsAi84ApiKey.Password.Trim();
                settings.SupabaseDbUrl = PbSettingsSupabaseDbUrl.Password.Trim();
                settings.ChromeProfilesDir = TxtSettingsChromeProfilesDir.Text.Trim();
                settings.OutputsDir = TxtSettingsOutputsDir.Text.Trim();
                settings.MaxConcurrentTasks = maxTasks;

                ConfigService.SaveSettings(settings);

                // Update python .env file in background
                _ = Task.Run(() => UpdatePythonEnvFile(settings.SupabaseDbUrl));
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to save application settings: {ex.Message}");
            }
        }

        private void UpdatePythonEnvFile(string databaseUrl)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "Chatgpt2Api"));
                if (!Directory.Exists(chatgpt2apiDir))
                {
                    chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "..\\..\\..\\Chatgpt2Api"));
                }

                if (!Directory.Exists(chatgpt2apiDir)) return;

                string envPath = Path.Combine(chatgpt2apiDir, ".env");
                if (!File.Exists(envPath)) return;

                var lines = File.ReadAllLines(envPath);
                bool found = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim().StartsWith("DATABASE_URL="))
                    {
                        lines[i] = $"DATABASE_URL={databaseUrl}";
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Trim().StartsWith("# DATABASE_URL="))
                        {
                            lines[i] = $"DATABASE_URL={databaseUrl}";
                            found = true;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    var newLines = new System.Collections.Generic.List<string>(lines) { $"DATABASE_URL={databaseUrl}" };
                    File.WriteAllLines(envPath, newLines);
                }
                else
                {
                    File.WriteAllLines(envPath, lines);
                }
                Log("[INFO] Python .env database connection updated.");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to update python .env: {ex.Message}");
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveApplicationSettings();
            MessageBox.Show("Đã lưu cấu hình hệ thống thành công và cập nhật API Backend!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadProfiles(); // reload profiles in case path changed
        }

        private void BtnBrowseChromeProfilesDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtSettingsChromeProfilesDir.Text = dialog.FolderName;
            }
        }

        private void BtnBrowseOutputsDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtSettingsOutputsDir.Text = dialog.FolderName;
            }
        }

        private async void BtnCheckAi84Key_Click(object sender, RoutedEventArgs e)
        {
            string apiKey = PbSettingsAi84ApiKey.Password.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                MessageBox.Show("Vui lòng nhập AI84 API Key trước khi kiểm tra.", "Yêu cầu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnCheckAi84Key.IsEnabled = false;
            BtnCheckAi84Key.Content = "Đang kiểm tra...";

            try
            {
                using var client = new System.Net.Http.HttpClient();
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.ai84.pro/v1/shared-voices?page_size=1");
                request.Headers.Add("xi-api-key", apiKey);

                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("AI84 API Key hoạt động chính xác!", "Hợp lệ", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    string content = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"AI84 API Key không hợp lệ.\nMã phản hồi: {(int)response.StatusCode} ({response.ReasonPhrase})", "Không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể kết nối để kiểm tra API Key:\n{ex.Message}", "Lỗi kết nối", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCheckAi84Key.IsEnabled = true;
                BtnCheckAi84Key.Content = "Kiểm tra Key";
            }
        }

        private async void BtnCheckSupabaseDb_Click(object sender, RoutedEventArgs e)
        {
            string dbUrl = PbSettingsSupabaseDbUrl.Password.Trim();
            if (string.IsNullOrEmpty(dbUrl))
            {
                MessageBox.Show("Vui lòng nhập Supabase Database URL trước khi kiểm tra.", "Yêu cầu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnCheckSupabaseDb.IsEnabled = false;
            BtnCheckSupabaseDb.Content = "Đang kết nối...";

            try
            {
                bool success = await Task.Run(() => RunDatabaseConnectionTest(dbUrl));
                if (success)
                {
                    MessageBox.Show("Kết nối tới Supabase Database thành công!", "Hợp lệ", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Kết nối tới Database thất bại. Vui lòng kiểm tra lại URL hoặc kết nối Internet.", "Không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi trong quá trình kiểm tra database:\n{ex.Message}", "Lỗi kiểm tra", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnCheckSupabaseDb.IsEnabled = true;
                BtnCheckSupabaseDb.Content = "Kiểm tra Kết nối";
            }
        }

        private bool RunDatabaseConnectionTest(string dbUrl)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "Chatgpt2Api"));
                if (!Directory.Exists(chatgpt2apiDir))
                {
                    chatgpt2apiDir = Path.GetFullPath(Path.Combine(appDir, "..\\..\\..\\Chatgpt2Api"));
                }

                if (!Directory.Exists(chatgpt2apiDir))
                {
                    return false;
                }

                string pythonExe = Path.Combine(chatgpt2apiDir, ".venv", "Scripts", "python.exe");
                if (!File.Exists(pythonExe))
                {
                    pythonExe = "python";
                }

                string testScript = Path.Combine(chatgpt2apiDir, "scripts", "test_storage.py");
                if (!File.Exists(testScript))
                {
                    return false;
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{testScript}\"",
                    WorkingDirectory = chatgpt2apiDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                startInfo.EnvironmentVariables["DATABASE_URL"] = dbUrl;
                startInfo.EnvironmentVariables["STORAGE_BACKEND"] = "postgres";
                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
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