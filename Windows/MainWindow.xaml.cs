using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using AssetAutomator.Services;

namespace AssetAutomator
{
    public partial class MainWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public bool IsSrtMethod2Enabled => !string.IsNullOrWhiteSpace(ConfigService.CurrentSettings.SubtitleApiUrl);
        public Visibility IsSrtMethod2Visible => IsSrtMethod2Enabled ? Visibility.Visible : Visibility.Collapsed;

        // Services
        private readonly BrowserService _browserService;
        private readonly HistoryService _historyService;
        private readonly ImagePoolService _imagePoolService;
        private readonly ChatGptService _chatGptService;
        private readonly UpdateService _updateService;
        private readonly LicenseService _licenseService;

        // Step services
        private readonly ThumbnailDownloadStep _step1;
        private readonly TranscriptExtractionStep _step2;
        private readonly ChatGptRewriteStep _step3;
        private readonly VoiceoverGenerationStep _step4;
        private readonly ImageGenerationStep _step5;

        private bool _isDarkMode = false;

        /// <summary>
        /// Replaces a SolidColorBrush in Application.Resources at runtime.
        /// All XAML elements using DynamicResource on the brush key will auto-update.
        /// </summary>
        private void SetThemeBrush(string brushKey, string hexColor)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                Application.Current.Resources[brushKey] = new System.Windows.Media.SolidColorBrush(color);
            }
            catch (Exception ex)
            {
                Log($"[ThemeError] Failed to set brush {brushKey}: {ex.Message}");
            }
        }

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isDarkMode = !_isDarkMode;

                if (_isDarkMode)
                {
                    // Dark Theme: replace SolidColorBrush objects - DynamicResource in XAML auto-updates
                    SetThemeBrush("CanvasBrush",       "#0C0F1D");
                    SetThemeBrush("InkBrush",          "#FFFFFF");
                    SetThemeBrush("BodyBrush",         "#D0D5EE");
                    SetThemeBrush("PrimaryBrush",      "#1E2442");
                    SetThemeBrush("PrimaryActiveBrush","#2A325C");
                    SetThemeBrush("PrimaryDisabledBrush","#3A4A6A");
                    SetThemeBrush("OnPrimaryBrush",    "#FFFFFF");
                    SetThemeBrush("SurfaceSoftBrush",  "#14192E");
                    SetThemeBrush("SurfaceCardBrush",  "#1E2644");
                    SetThemeBrush("SurfaceStrongBrush","#2B355A");
                    SetThemeBrush("HairlineBrush",     "#34406A");

                    if (BtnThemeToggle?.Template?.FindName("TxtThemeIcon", BtnThemeToggle) is TextBlock txtIcon) txtIcon.Text = "☀️";
                    if (BtnThemeToggle?.Template?.FindName("TxtThemeLabel", BtnThemeToggle) is TextBlock txtLabel) txtLabel.Text = "Light Mode";
                }
                else
                {
                    // Light Theme: deeper muted slate gray palette
                    SetThemeBrush("CanvasBrush",       "#E2E8F0");
                    SetThemeBrush("InkBrush",          "#0F172A");
                    SetThemeBrush("BodyBrush",         "#1E293B");
                    SetThemeBrush("PrimaryBrush",      "#0F172A");
                    SetThemeBrush("PrimaryActiveBrush","#1E293B");
                    SetThemeBrush("PrimaryDisabledBrush","#94A3B8");
                    SetThemeBrush("OnPrimaryBrush",    "#FFFFFF");
                    SetThemeBrush("SurfaceSoftBrush",  "#CBD5E1");
                    SetThemeBrush("SurfaceCardBrush",  "#FFFFFF");
                    SetThemeBrush("SurfaceStrongBrush","#94A3B8");
                    SetThemeBrush("HairlineBrush",     "#CBD5E1");
                    SetThemeBrush("MutedSoftBrush",    "#475569");

                    if (BtnThemeToggle?.Template?.FindName("TxtThemeIcon", BtnThemeToggle) is TextBlock txtIcon) txtIcon.Text = "🌙";
                    if (BtnThemeToggle?.Template?.FindName("TxtThemeLabel", BtnThemeToggle) is TextBlock txtLabel) txtLabel.Text = "Dark Mode";
                }
                ThemeService.Apply(_isDarkMode);
            }
            catch (Exception ex)
            {
                Log($"[ThemeToggle] Error: {ex.Message}");
            }
        }

        private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            _updateService.CheckForUpdates(isManualCheck: true);
        }

        private async void BtnLicense_Click(object sender, RoutedEventArgs e)
        {
            var licenseWin = new Windows.LicenseWindow(_licenseService)
            {
                Owner = this
            };
            licenseWin.ShowDialog();

            var recheck = await _licenseService.VerifyAsync();
            if (!recheck.Success)
            {
                MessageBox.Show("Ứng dụng chưa được kích hoạt bản quyền hợp lệ hoặc đã bị hủy. Ứng dụng sẽ tự động đóng.", 
                    "Yêu cầu bản quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
            }
        }

        private void LicenseService_OnSessionInvalidated(object? sender, string reason)
        {
            Dispatcher.Invoke(async () =>
            {
                MessageBox.Show($"CẢNH BÁO BẢN QUYỀN:\n{reason}\n\nỨng dụng sẽ mở cửa sổ Quản Lý Bản Quyền.", "Phiên bản quyền bị hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
                
                var win = new Windows.LicenseWindow(_licenseService) { Owner = this };
                win.ShowDialog();

                var recheck = await _licenseService.VerifyAsync();
                if (!recheck.Success)
                {
                    MessageBox.Show("Phiên bản quyền của bạn đã kết thúc. Ứng dụng sẽ tự động đóng.", 
                        "Tắt ứng dụng", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                }
            });
        }

        // UI state
        private DateTime _lastUiUpdateTime = DateTime.MinValue;

        public ObservableCollection<AutomationTask> Tasks { get; set; } = new ObservableCollection<AutomationTask>();
        public ObservableCollection<string> ProfileList { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> LanguageList { get; set; } = new ObservableCollection<string>(AppConstants.Languages);
        public ObservableCollection<string> HistoryDates { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<HistoryTaskModel> HistoryTasks { get; set; } = new ObservableCollection<HistoryTaskModel>();

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _browserService = new BrowserService(Log);
            _historyService = new HistoryService(Log);
            _chatGptService = new ChatGptService();
            _imagePoolService = new ImagePoolService();
            _imagePoolService.LogTask = LogTask;
            _imagePoolService.OnPoolStateChanged += UpdatePoolUi;
            _updateService = new UpdateService(Log);
            _licenseService = new LicenseService();
            _licenseService.OnSessionInvalidated += LicenseService_OnSessionInvalidated;

            // Initialize step services
            _step1 = new ThumbnailDownloadStep();
            _step2 = new TranscriptExtractionStep();
            _step3 = new ChatGptRewriteStep(_chatGptService);
            _step4 = new VoiceoverGenerationStep();
            _step5 = new ImageGenerationStep(_imagePoolService);

            DgridTasks.ItemsSource = Tasks;
            DataContext = this;
            Log("Application started. Ready to run tasks.");
            LoadProfiles();
            LoadApplicationSettings();
            LoadHistoryDates();
            InitializeBatchImageGenTab();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveApplicationSettings();
            _licenseService.Dispose();
        }

        private void UpdatePoolUi()
        {
            var now = DateTime.Now;
            bool shouldUpdateDataGrid = false;
            if ((now - _lastUiUpdateTime).TotalMilliseconds >= 250)
            {
                shouldUpdateDataGrid = true;
                _lastUiUpdateTime = now;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var stats = _imagePoolService.GetPoolStats();

                TxtPoolRunningWorkers.Text = stats.ActiveWorkers.ToString();
                TxtPoolMaxWorkers.Text = stats.MaxWorkers.ToString();
                TxtPoolWaitingRequests.Text = stats.Waiting.ToString();
                TxtPoolProcessingRequests.Text = stats.Processing.ToString();
                TxtPoolFinishedRequests.Text = stats.Finished.ToString();
                TxtPoolAvgTime.Text = stats.AvgSeconds.ToString("F1");

                if (shouldUpdateDataGrid || stats.Waiting == 0 || stats.Processing == 0)
                {
                    DgridPoolRequests.ItemsSource = _imagePoolService.GetOrderedRequests();
                }
            }));
        }

        private void BtnRefreshPool_Click(object sender, RoutedEventArgs e)
        {
            UpdatePoolUi();
        }

        private void LoadApplicationSettings()
        {
            try
            {
                var settings = ConfigService.LoadSettings();
                PbSettingsAi84ApiKey.Password = settings.Ai84ApiKey;
                PbSettingsSupabaseDbUrl.Password = settings.SupabaseDbUrl;
                TxtSettingsImageApiUrl.Text = settings.ImageApiUrl;
                TxtSettingsSubtitleApiUrl.Text = settings.SubtitleApiUrl;
                PbSettingsImageApiKey.Password = settings.ImageApiKey;
                TxtSettingsChromeProfilesDir.Text = settings.ChromeProfilesDir;
                TxtSettingsOutputsDir.Text = settings.OutputsDir;
                TxtSettingsMaxConcurrentTasks.Text = settings.MaxConcurrentTasks.ToString();
                TxtSettingsProxiesFilePath.Text = settings.ProxiesFilePath;
                if (!string.IsNullOrEmpty(settings.ManualProxiesFilePath) && File.Exists(settings.ManualProxiesFilePath))
                {
                    TxtSettingsManualProxies.Text = File.ReadAllText(settings.ManualProxiesFilePath);
                }
                else
                {
                    TxtSettingsManualProxies.Text = string.Empty;
                }
                OnPropertyChanged(nameof(IsSrtMethod2Enabled));
                OnPropertyChanged(nameof(IsSrtMethod2Visible));
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
                settings.ImageApiUrl = TxtSettingsImageApiUrl.Text.Trim();
                settings.SubtitleApiUrl = TxtSettingsSubtitleApiUrl.Text.Trim();
                settings.ImageApiKey = PbSettingsImageApiKey.Password.Trim();
                settings.ChromeProfilesDir = TxtSettingsChromeProfilesDir.Text.Trim();
                settings.OutputsDir = TxtSettingsOutputsDir.Text.Trim();
                settings.MaxConcurrentTasks = maxTasks;
                settings.ProxiesFilePath = TxtSettingsProxiesFilePath.Text.Trim();

                if (!string.IsNullOrEmpty(settings.ManualProxiesFilePath))
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(settings.ManualProxiesFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        File.WriteAllText(settings.ManualProxiesFilePath, TxtSettingsManualProxies.Text);
                    }
                    catch (Exception ex)
                    {
                        Log($"[ERROR] Failed to save manual proxies file: {ex.Message}");
                    }
                }

                ConfigService.EnsureDefaults(settings);
                ConfigService.SaveSettings(settings);
                OnPropertyChanged(nameof(IsSrtMethod2Enabled));
                OnPropertyChanged(nameof(IsSrtMethod2Visible));
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to save application settings: {ex.Message}");
            }
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveApplicationSettings();
            MessageBox.Show("Đã lưu cấu hình hệ thống thành công và cập nhật API Backend!", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadProfiles();
        }

        private void BtnExportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveApplicationSettings();

                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    FileName = "appsettings_backup.json",
                    Title = "Xuất cấu hình hệ thống"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    string json = System.Text.Json.JsonSerializer.Serialize(ConfigService.CurrentSettings, options);
                    File.WriteAllText(saveFileDialog.FileName, json);
                    MessageBox.Show("Xuất cấu hình hệ thống thành công!", "Xuất thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất cấu hình: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERROR] Failed to export settings: {ex.Message}");
            }
        }

        private void BtnImportSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json",
                    Title = "Nhập cấu hình hệ thống"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string json = File.ReadAllText(openFileDialog.FileName);
                    var imported = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                    if (imported != null)
                    {
                        ConfigService.SaveSettings(imported);
                        LoadApplicationSettings();
                        LoadProfiles();
                        MessageBox.Show("Nhập cấu hình hệ thống thành công và đã áp dụng cấu hình mới!", "Nhập thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Tệp cấu hình không hợp lệ hoặc rỗng.", "Lỗi nhập", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi nhập cấu hình: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERROR] Failed to import settings: {ex.Message}");
            }
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

        private void BtnBrowseProxiesFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Chọn tệp Proxies JSON"
            };
            if (dialog.ShowDialog() == true)
            {
                TxtSettingsProxiesFilePath.Text = dialog.FileName;
            }
        }

        private async void BtnTestProxies_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtSettingsProxiesFilePath.Text.Trim();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                MessageBox.Show("File proxy không tồn tại hoặc đường dẫn trống.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnTestProxies.IsEnabled = false;
            BtnTestProxies.Content = "Testing...";
            Log($"Starting testing proxies from file: {path}");

            try
            {
                string jsonContent = await File.ReadAllTextAsync(path);
                using var doc = System.Text.Json.JsonDocument.Parse(jsonContent);
                if (!doc.RootElement.TryGetProperty("proxies", out var proxiesArray) || proxiesArray.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    MessageBox.Show("File json không chứa mảng 'proxies'.", "Lỗi định dạng", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var proxyList = new System.Collections.Generic.List<string>();
                foreach (var item in proxiesArray.EnumerateArray())
                {
                    if (item.TryGetProperty("proxy", out var proxyProp))
                    {
                        string val = proxyProp.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            proxyList.Add(val);
                        }
                    }
                }

                if (proxyList.Count == 0)
                {
                    MessageBox.Show("Không tìm thấy proxy nào trong file.", "Trống", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Log($"Parsed {proxyList.Count} proxies. Testing first few proxies...");
                
                int aliveCount = 0;
                int deadCount = 0;
                
                var tasks = new System.Collections.Generic.List<Task<(string proxy, bool success)>>();
                int limit = Math.Min(proxyList.Count, 30); // Test up to 30 proxies to keep it fast
                for (int i = 0; i < limit; i++)
                {
                    string proxyStr = proxyList[i];
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var handler = new System.Net.Http.HttpClientHandler();
                            handler.Proxy = new System.Net.WebProxy(proxyStr);
                            handler.UseProxy = true;
                            
                            using var client = new System.Net.Http.HttpClient(handler);
                            client.Timeout = TimeSpan.FromSeconds(5);
                            
                            var response = await client.GetAsync("https://www.google.com");
                            return (proxyStr, response.IsSuccessStatusCode);
                        }
                        catch
                        {
                            return (proxyStr, false);
                        }
                    }));
                }

                var results = await Task.WhenAll(tasks);
                foreach (var res in results)
                {
                    if (res.success)
                    {
                        aliveCount++;
                        Log($"[ALIVE] Proxy works: {res.proxy}");
                    }
                    else
                    {
                        deadCount++;
                    }
                }

                MessageBox.Show($"Đã test {limit} proxies đầu tiên.\nSống: {aliveCount}\nChết/Không phản hồi: {deadCount}", "Kết quả Test Proxy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi test proxy: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"[ERROR] Proxy test error: {ex.Message}");
            }
            finally
            {
                BtnTestProxies.IsEnabled = true;
                BtnTestProxies.Content = "Test Proxies";
            }
        }

        private async void BtnTestManualProxies_Click(object sender, RoutedEventArgs e)
        {
            string content = TxtSettingsManualProxies.Text;
            var rawLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var proxyList = rawLines.Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList();

            if (proxyList.Count == 0)
            {
                MessageBox.Show("Vui lòng nhập danh sách proxy để kiểm tra.", "Trống", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnTestManualProxies.IsEnabled = false;
            BtnTestManualProxies.Content = "Đang kiểm tra...";
            Log($"Starting testing {proxyList.Count} manual proxies...");

            try
            {
                var tasks = new System.Collections.Generic.List<Task<(string proxy, bool success, string error)>>();
                foreach (var proxyStr in proxyList)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var webProxy = ProxyHelper.ParseProxy(proxyStr);
                            if (webProxy == null)
                            {
                                return (proxyStr, false, "Lỗi phân tích cú pháp");
                            }

                            var handler = new System.Net.Http.HttpClientHandler
                            {
                                Proxy = webProxy,
                                UseProxy = true
                            };

                            using var client = new System.Net.Http.HttpClient(handler);
                            client.Timeout = TimeSpan.FromSeconds(10); // Timeout 10s as requested by user

                            using var response = await client.GetAsync("https://www.youtube.com");
                            if (response.IsSuccessStatusCode)
                            {
                                return (proxyStr, true, "OK");
                            }
                            else
                            {
                                return (proxyStr, false, $"Status code: {(int)response.StatusCode}");
                            }
                        }
                        catch (Exception ex)
                        {
                            return (proxyStr, false, ex.Message);
                        }
                    }));
                }

                var results = await Task.WhenAll(tasks);
                
                int onlineCount = 0;
                int offlineCount = 0;
                var displayList = new System.Collections.Generic.List<ProxyTestResultItem>();

                foreach (var res in results)
                {
                    var item = new ProxyTestResultItem
                    {
                        Proxy = res.proxy,
                        Status = res.success ? "ONLINE" : "OFFLINE",
                        Response = res.error
                    };
                    displayList.Add(item);

                    if (res.success)
                    {
                        onlineCount++;
                    }
                    else
                    {
                        offlineCount++;
                    }
                }

                var resultWindow = new ProxyTestResultWindow(displayList, onlineCount, offlineCount);
                resultWindow.Owner = this;
                resultWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Có lỗi xảy ra: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnTestManualProxies.IsEnabled = true;
                BtnTestManualProxies.Content = "Kiểm tra Proxy thủ công";
            }
        }

        private void FilterFields_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void FilterCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
        {
            TxtFilterVideoUrl.Text = string.Empty;
            TxtFilterLanguage.Text = string.Empty;
            TxtFilterVoiceId.Text = string.Empty;
            ChkFilterT.IsChecked = false;
            ChkFilterR.IsChecked = false;
            ChkFilterW.IsChecked = false;
            ChkFilterV.IsChecked = false;
            ChkFilterS.IsChecked = false;
            ChkFilterG.IsChecked = false;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(Tasks);
            if (view == null) return;

            string filterVideo = TxtFilterVideoUrl.Text.Trim();
            string filterLang = TxtFilterLanguage.Text.Trim();
            string filterVoice = TxtFilterVoiceId.Text.Trim();

            bool chkT = ChkFilterT.IsChecked == true;
            bool chkR = ChkFilterR.IsChecked == true;
            bool chkW = ChkFilterW.IsChecked == true;
            bool chkV = ChkFilterV.IsChecked == true;
            bool chkS = ChkFilterS.IsChecked == true;
            bool chkG = ChkFilterG.IsChecked == true;
            bool anyFailedFilter = chkT || chkR || chkW || chkV || chkS || chkG;

            if (string.IsNullOrEmpty(filterVideo) && string.IsNullOrEmpty(filterLang) && string.IsNullOrEmpty(filterVoice) && !anyFailedFilter)
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = obj =>
                {
                    if (obj is AutomationTask task)
                    {
                        if (!string.IsNullOrEmpty(filterVideo) && (task.VideoUrl == null || !task.VideoUrl.Contains(filterVideo, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        if (!string.IsNullOrEmpty(filterLang) && (task.TargetLanguage == null || !task.TargetLanguage.Contains(filterLang, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        if (!string.IsNullOrEmpty(filterVoice) && (task.VoiceId == null || !task.VoiceId.Contains(filterVoice, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        
                        if (anyFailedFilter)
                        {
                            bool matches = false;
                            if (chkT && task.Step1Status == "Failed") matches = true;
                            if (chkR && task.Step2Status == "Failed") matches = true;
                            if (chkW && task.Step3Status == "Failed") matches = true;
                            if (chkV && task.Step4Status == "Failed") matches = true;
                            if (chkS && task.StepSrtStatus == "Failed") matches = true;
                            if (chkG && task.Step5Status == "Failed") matches = true;
                            if (!matches) return false;
                        }
                        return true;
                    }
                    return false;
                };
            }
        }

        private void HistoryFilterFields_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyHistoryFilters();
        }

        private void HistoryFilterCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyHistoryFilters();
        }

        private void BtnHistoryClearFilters_Click(object sender, RoutedEventArgs e)
        {
            TxtHistoryFilterVideoUrl.Text = string.Empty;
            TxtHistoryFilterLanguage.Text = string.Empty;
            TxtHistoryFilterVoiceId.Text = string.Empty;
            ChkHistoryFilterT.IsChecked = false;
            ChkHistoryFilterR.IsChecked = false;
            ChkHistoryFilterW.IsChecked = false;
            ChkHistoryFilterV.IsChecked = false;
            ChkHistoryFilterS.IsChecked = false;
            ChkHistoryFilterG.IsChecked = false;
            ApplyHistoryFilters();
        }

        public void ApplyHistoryFilters()
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(HistoryTasks);
            if (view == null) return;

            string filterVideo = TxtHistoryFilterVideoUrl.Text.Trim();
            string filterLang = TxtHistoryFilterLanguage.Text.Trim();
            string filterVoice = TxtHistoryFilterVoiceId.Text.Trim();

            bool chkT = ChkHistoryFilterT.IsChecked == true;
            bool chkR = ChkHistoryFilterR.IsChecked == true;
            bool chkW = ChkHistoryFilterW.IsChecked == true;
            bool chkV = ChkHistoryFilterV.IsChecked == true;
            bool chkS = ChkHistoryFilterS.IsChecked == true;
            bool chkG = ChkHistoryFilterG.IsChecked == true;
            bool anyFailedFilter = chkT || chkR || chkW || chkV || chkS || chkG;

            if (string.IsNullOrEmpty(filterVideo) && string.IsNullOrEmpty(filterLang) && string.IsNullOrEmpty(filterVoice) && !anyFailedFilter)
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = obj =>
                {
                    if (obj is HistoryTaskModel task)
                    {
                        if (!string.IsNullOrEmpty(filterVideo) && (task.VideoUrl == null || !task.VideoUrl.Contains(filterVideo, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        if (!string.IsNullOrEmpty(filterLang) && (task.TargetLanguage == null || !task.TargetLanguage.Contains(filterLang, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        if (!string.IsNullOrEmpty(filterVoice) && (task.VoiceId == null || !task.VoiceId.Contains(filterVoice, StringComparison.OrdinalIgnoreCase)))
                            return false;
                        
                        if (anyFailedFilter)
                        {
                            bool matches = false;
                            if (chkT && task.Step1Status == "Failed") matches = true;
                            if (chkR && task.Step2Status == "Failed") matches = true;
                            if (chkW && task.Step3Status == "Failed") matches = true;
                            if (chkV && task.Step4Status == "Failed") matches = true;
                            if (chkS && task.StepSrtStatus == "Failed") matches = true;
                            if (chkG && task.Step5Status == "Failed") matches = true;
                            if (!matches) return false;
                        }
                        return true;
                    }
                    return false;
                };
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RunSystemCheckAsync(showIfAllOk: false);
            await CheckLicenseOnStartupAsync();
        }

        private async Task CheckLicenseOnStartupAsync()
        {
            var resp = await _licenseService.VerifyAsync();
            if (!resp.Success)
            {
                Log($"[License] Startup check result: {resp.Message}");
                var win = new Windows.LicenseWindow(_licenseService) { Owner = this };
                win.ShowDialog();

                var recheck = await _licenseService.VerifyAsync();
                if (!recheck.Success)
                {
                    MessageBox.Show("Ứng dụng chưa được kích hoạt bản quyền hợp lệ. Ứng dụng sẽ tự động đóng.", 
                        "Yêu cầu bản quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                }
            }
            else
            {
                Log($"[License] Verification successful. Remaining days: {resp.RemainingDays}");
            }
        }

        public async Task<bool> EnsureLicenseValidAsync()
        {
            var resp = await _licenseService.VerifyAsync();
            if (!resp.Success)
            {
                Log($"[LicenseGuard] Verification check failed: {resp.Message}");
                var win = new Windows.LicenseWindow(_licenseService) { Owner = this };
                win.ShowDialog();

                var recheck = await _licenseService.VerifyAsync();
                if (!recheck.Success)
                {
                    MessageBox.Show("Ứng dụng chưa được kích hoạt bản quyền hợp lệ. Ứng dụng sẽ tự động đóng.", 
                        "Chưa kích hoạt bản quyền", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                    return false;
                }
            }
            return true;
        }

        private async void BtnCheckRequirements_Click(object sender, RoutedEventArgs e)
        {
            await RunSystemCheckAsync(showIfAllOk: true);
        }

        private async Task RunSystemCheckAsync(bool showIfAllOk)
        {
            Log("Running system requirements check...");
            var result = await SystemRequirementsChecker.CheckAsync();

            string statusMessage = "=== KẾT QUẢ KIỂM TRA HỆ THỐNG ===\n";
            statusMessage += $"1. Google Chrome: {(result.IsChromeInstalled ? "Đã cài đặt" : "CHƯA CÀI ĐẶT (BẮT BUỘC)")}\n";
            if (result.IsChromeInstalled)
            {
                statusMessage += $"   -> Đường dẫn: {result.ChromePath}\n";
            }
            statusMessage += $"2. Thư viện Playwright: {(result.IsPlaywrightOk ? "Hoạt động tốt" : "LỖI CHƯA SETUP")}\n";
            if (!result.IsPlaywrightOk)
            {
                statusMessage += $"   -> Chi tiết: {result.PlaywrightError}\n";
            }
            statusMessage += $"3. Python: {(result.IsPythonInstalled ? $"Đã cài đặt ({result.PythonVersion})" : "CHƯA CÀI ĐẶT hoặc chưa thêm vào PATH (Yêu cầu để tạo video)")}\n";

            Log(statusMessage);

            if (!result.IsAllOk || showIfAllOk)
            {
                string title = result.IsAllOk ? "Kiểm tra hệ thống hoàn tất (OK)" : "Cảnh báo: Thiếu yêu cầu hệ thống!";
                string userMessage = result.IsAllOk 
                    ? "Mọi yêu cầu hệ thống (Chrome, Playwright, Python) đã sẵn sàng để chạy tool."
                    : "Ứng dụng phát hiện máy tính này chưa được cài đặt đầy đủ các yêu cầu cần thiết:\n\n" +
                      $"- Chrome: {(result.IsChromeInstalled ? "OK" : "Chưa cài đặt. Vui lòng tải và cài đặt Google Chrome.")}\n" +
                      $"- Playwright: {(result.IsPlaywrightOk ? "OK" : $"Lỗi: {result.PlaywrightError}. Vui lòng đảm bảo đã copy thư mục 'runtimes' đi kèm bản build.")}\n" +
                      $"- Python: {(result.IsPythonInstalled ? "OK" : "Chưa cài đặt hoặc chưa thêm vào biến môi trường PATH. Cần thiết để tạo video.")}\n\n" +
                      "Nếu bạn vừa cài đặt/sửa đổi xong, hãy nhấn nút 'Kiểm Tra Hệ Thống' trong phần Cấu Hình để quét lại.";

                MessageBox.Show(userMessage, title, MessageBoxButton.OK, 
                    result.IsAllOk ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }

        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
