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

        // Chrome window slots
        private static readonly bool[] _activeBrowserSlots = new bool[32];
        private static readonly object _browserSlotsLock = new object();

        // Image Generation Request Pool
        private readonly System.Collections.Generic.List<ImageGenRequest> _imageRequestPool = new System.Collections.Generic.List<ImageGenRequest>();
        private readonly object _poolLock = new object();
        private int _maxImageWorkers = 1;
        private int _activeImageWorkers = 0;
        private DateTime _lastUiUpdateTime = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            DgridTasks.ItemsSource = Tasks;
            DataContext = this;
            Log("Application started. Ready to run tasks.");
            LoadProfiles();
            LoadApplicationSettings();
            LoadHistoryDates();

            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveApplicationSettings();
        }

        public async Task EnqueueImageRequestAsync(ImageGenRequest request)
        {
            try
            {
                int calculatedWorkers = Math.Max(1, Math.Min(10, ConfigService.CurrentSettings.MaxConcurrentTasks * 2));
                lock (_poolLock)
                {
                    _maxImageWorkers = calculatedWorkers;
                }
            }
            catch (Exception ex)
            {
                Log($"[POOL] Error determining max workers: {ex.Message}. Falling back to default workers.");
            }

            lock (_poolLock)
            {
                _imageRequestPool.Add(request);
                LogTask(request.Task, $"[POOL] Enqueued image request: {System.IO.Path.GetFileName(request.SavePath)} (Status: {request.Status})");
                
                while (_activeImageWorkers < _maxImageWorkers)
                {
                    _activeImageWorkers++;
                    _ = Task.Run(async () => await ImageWorkerLoopAsync());
                }
            }
            UpdatePoolUi();
        }

        private async Task ImageWorkerLoopAsync()
        {
            while (true)
            {
                ImageGenRequest? req = null;
                lock (_poolLock)
                {
                    req = GetNextRequestToProcess();
                    if (req == null)
                    {
                        _activeImageWorkers--;
                        UpdatePoolUi();
                        break;
                    }
                }

                try
                {
                    req.StartedAt = DateTime.Now;
                    UpdatePoolUi();

                    LogTask(req.Task, $"[POOL] Starting API generation for: {System.IO.Path.GetFileName(req.SavePath)}");
                    await EditImageViaApiAsync(req);
                    
                    lock (_poolLock)
                    {
                        req.Status = "Done";
                        req.FinishedAt = DateTime.Now;
                    }
                    req.Tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    lock (_poolLock)
                    {
                        req.Status = "Failed";
                        req.FinishedAt = DateTime.Now;
                        req.ErrorMessage = ex.Message;
                    }
                    LogTask(req.Task, $"[POOL] [ERROR] Image generation failed for {System.IO.Path.GetFileName(req.SavePath)}: {ex.Message}");
                    req.Tcs.SetException(ex);
                }
                finally
                {
                    UpdatePoolUi();
                }

                await Task.Delay(5000);
            }
        }

        private ImageGenRequest? GetNextRequestToProcess()
        {
            var inProgressTaskIds = _imageRequestPool
                .Where(r => r.Status == "Processing")
                .Select(r => r.Task.VideoId)
                .Distinct()
                .ToList();

            foreach (var taskId in inProgressTaskIds)
            {
                var nextInSameTask = _imageRequestPool.FirstOrDefault(r => r.Task.VideoId == taskId && r.Status == "Waiting");
                if (nextInSameTask != null)
                {
                    nextInSameTask.Status = "Processing";
                    return nextInSameTask;
                }
            }

            var nextRequest = _imageRequestPool
                .Where(r => r.Status == "Waiting")
                .OrderBy(r => r.EnqueuedAt)
                .FirstOrDefault();

            if (nextRequest != null)
            {
                nextRequest.Status = "Processing";
            }
            return nextRequest;
        }

        private void UpdatePoolUi()
        {
            var now = DateTime.Now;
            bool shouldUpdateDataGrid = false;
            lock (_poolLock)
            {
                if ((now - _lastUiUpdateTime).TotalMilliseconds >= 250)
                {
                    shouldUpdateDataGrid = true;
                    _lastUiUpdateTime = now;
                }
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                lock (_poolLock)
                {
                    int waiting = _imageRequestPool.Count(r => r.Status == "Waiting");
                    int processing = _imageRequestPool.Count(r => r.Status == "Processing");
                    int finished = _imageRequestPool.Count(r => r.Status == "Done" || r.Status == "Failed");

                    TxtPoolRunningWorkers.Text = _activeImageWorkers.ToString();
                    TxtPoolMaxWorkers.Text = _maxImageWorkers.ToString();
                    TxtPoolWaitingRequests.Text = waiting.ToString();
                    TxtPoolProcessingRequests.Text = processing.ToString();
                    TxtPoolFinishedRequests.Text = finished.ToString();

                    var processedRequests = _imageRequestPool.Where(r => r.StartedAt != null && r.FinishedAt != null).ToList();
                    double avgSeconds = 0;
                    if (processedRequests.Count > 0)
                    {
                        avgSeconds = processedRequests.Average(r => (r.FinishedAt!.Value - r.StartedAt!.Value).TotalSeconds);
                    }
                    TxtPoolAvgTime.Text = avgSeconds.ToString("F1");

                    // Throttle DataGrid binding updates to prevent UI stuttering, always update on idle/done
                    if (shouldUpdateDataGrid || waiting == 0 || processing == 0)
                    {
                        DgridPoolRequests.ItemsSource = _imageRequestPool.OrderByDescending(r => r.EnqueuedAt).ToList();
                    }
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
                PbSettingsImageApiKey.Password = settings.ImageApiKey;
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
                settings.ImageApiUrl = TxtSettingsImageApiUrl.Text.Trim();
                settings.ImageApiKey = PbSettingsImageApiKey.Password.Trim();
                settings.ChromeProfilesDir = TxtSettingsChromeProfilesDir.Text.Trim();
                settings.OutputsDir = TxtSettingsOutputsDir.Text.Trim();
                settings.MaxConcurrentTasks = maxTasks;

                ConfigService.SaveSettings(settings);
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
            LoadProfiles(); // reload profiles in case path changed
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



        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
        }
    }

    public class ImageGenRequest
    {
        public string ApiUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string SavePath { get; set; } = string.Empty;
        public AutomationTask Task { get; set; } = null!;
        public System.Threading.Tasks.TaskCompletionSource<bool> Tcs { get; set; } = new System.Threading.Tasks.TaskCompletionSource<bool>();
        public string Status { get; set; } = "Waiting"; // "Waiting", "Processing", "Done", "Failed"
        public DateTime EnqueuedAt { get; set; } = DateTime.Now;
        public string TaskId => Task?.Id.ToString().Substring(0, 8) ?? string.Empty;
        public string VideoId => Task?.VideoId ?? string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string CreatedTimeFormatted => EnqueuedAt.ToString("HH:mm:ss");
        public string FinishedTimeFormatted => FinishedAt?.ToString("HH:mm:ss") ?? string.Empty;
    }
}