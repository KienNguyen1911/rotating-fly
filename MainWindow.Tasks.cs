using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;

namespace AutoCreateImage
{
    public partial class MainWindow : Window
    {
        private AutomationTask? _currentLogTask;
        private double _sidebarWidth = 420;
        private bool _isDraggingSidebar = false;
        private double _dragStartX;

        private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is AutomationTask task)
            {
                _ = Task.Run(() => SaveTaskToHistoryAsync(task));
            }
        }

        private void LogTask(AutomationTask task, string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            task.Logs += formattedMessage;

            // If the sidebar is open and showing this task's logs, append to it in real-time
            Dispatcher.Invoke(() =>
            {
                if (SidebarLogs.Visibility == Visibility.Visible && TxtSidebarLog.DataContext == task)
                {
                    TxtSidebarLog.AppendText(formattedMessage);
                    TxtSidebarLog.ScrollToEnd();
                }
            });
        }

        private void BtnAddTask_Click(object sender, RoutedEventArgs e)
        {
            string selectedProfile = ProfileList.Count > 0 ? ProfileList[0] : string.Empty;

            var task = new AutomationTask
            {
                VideoUrl = string.Empty,
                TargetLanguage = string.Empty,
                VoiceId = string.Empty,
                Step1 = true,
                Step2 = true,
                Step3 = true,
                Step4 = true,
                Step5 = true,
                SelectedProfile = selectedProfile,
                Status = "Pending",
                CreatedAt = DateTime.Now
            };

            task.PropertyChanged += Task_PropertyChanged;
            Tasks.Insert(0, task);
            _ = Task.Run(() => SaveTaskToHistoryAsync(task));
            Log("Created new empty task in the table.");
        }

        private void BtnAddBulkTasks_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new BulkTaskWindow
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedProfile = ProfileList.Count > 0 ? ProfileList[0] : string.Empty;
                string apiKey = ConfigService.CurrentSettings.Ai84ApiKey;

                int count = 0;
                foreach (var entry in dialog.TasksToCreate)
                {
                    var task = new AutomationTask
                    {
                        VideoUrl = entry.Url,
                        TargetLanguage = string.Empty,
                        VoiceId = entry.VoiceId,
                        Step1 = true,
                        Step2 = true,
                        Step3 = true,
                        Step4 = true,
                        Step5 = true,
                        SelectedProfile = selectedProfile,
                        Status = "Pending",
                        CreatedAt = DateTime.Now
                    };

                    task.PropertyChanged += Task_PropertyChanged;
                    Tasks.Insert(0, task);
                    _ = Task.Run(() => SaveTaskToHistoryAsync(task));

                    if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(entry.VoiceId))
                    {
                        _ = Task.Run(() => ResolveTaskLanguageAsync(task, apiKey));
                    }
                    count++;
                }

                Log($"Bulk created {count} tasks from list.");
            }
        }

        private async Task ResolveTaskLanguageAsync(AutomationTask task, string apiKey)
        {
            if (string.IsNullOrEmpty(task.VoiceId) || !string.IsNullOrEmpty(task.TargetLanguage)) return;
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"https://api.ai84.pro/v1/shared-voices?page_size=10&search={Uri.EscapeDataString(task.VoiceId)}");
                request.Headers.Add("xi-api-key", apiKey);
                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var result = System.Text.Json.JsonSerializer.Deserialize<SharedVoicesResponse>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var voice = result?.voices?.FirstOrDefault(v => v.voice_id == task.VoiceId);
                    if (voice != null)
                    {
                        task.TargetLanguage = LanguageHelper.FormatLanguage(voice.language);
                    }
                }
            }
            catch { /* Ignore background errors */ }
        }

        private async void BtnRunSingleTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutomationTask task)
            {
                if (task.Status == "Running" || task.Status.StartsWith("Step"))
                {
                    MessageBox.Show("This task is already running.", "Task Running", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(task.VideoUrl) || string.IsNullOrEmpty(task.VideoId) || task.VideoId == "unknown")
                {
                    MessageBox.Show("Please enter a valid YouTube Video URL for this task.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(task.SelectedProfile))
                {
                    MessageBox.Show("Please select a Chrome Profile for this task.", "Profile Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SaveApplicationSettings();
                btn.IsEnabled = false;
                try
                {
                    await Task.Run(async () =>
                    {
                        try
                        {
                            await RunSingleVideoFlowAsync(task);
                        }
                        catch (Exception ex)
                        {
                            task.Status = "Failed";
                            LogTask(task, $"[ERROR] Task failed: {ex.Message}");
                        }
                    });
                }
                finally
                {
                    btn.IsEnabled = true;
                }
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var pendingTasks = Tasks.Where(t => t.Status == "Pending" || t.Status == "Failed").ToArray();
            if (pendingTasks.Length == 0)
            {
                MessageBox.Show("No pending or failed tasks to run.", "No Tasks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var task in pendingTasks)
            {
                if (string.IsNullOrEmpty(task.SelectedProfile))
                {
                    MessageBox.Show($"Please select a Chrome Profile for the task with Video ID: {task.VideoId}.", "Profile Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            SaveApplicationSettings();
            BtnRun.IsEnabled = false;
            BtnAddTask.IsEnabled = false;
            try
            {
                int maxConcurrent = ConfigService.CurrentSettings.MaxConcurrentTasks;
                if (maxConcurrent <= 0) maxConcurrent = 4;
                Log($"[FLOW] Starting batch execution for {pendingTasks.Length} tasks (Max {maxConcurrent} concurrent threads)...");

                await Task.Run(async () =>
                {
                    using var concurrencySemaphore = new System.Threading.SemaphoreSlim(maxConcurrent, maxConcurrent);
                    var tasks = pendingTasks.Select(async task =>
                    {
                        await concurrencySemaphore.WaitAsync();
                        try
                        {
                            await RunSingleVideoFlowAsync(task);
                        }
                        catch (Exception ex)
                        {
                            task.Status = "Failed";
                            LogTask(task, $"[ERROR] Task failed: {ex.Message}");
                        }
                        finally
                        {
                            concurrencySemaphore.Release();
                        }
                    });

                    await Task.WhenAll(tasks);
                    Log("[FLOW] Batch execution finished.");
                });
            }
            finally
            {
                BtnRun.IsEnabled = true;
                BtnAddTask.IsEnabled = true;
            }
        }

        private void BtnViewTaskLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutomationTask task)
            {
                _currentLogTask = task;
                TxtSidebarLog.DataContext = task;
                TxtSidebarLog.Text = task.Logs;
                TxtSidebarLog.ScrollToEnd();
                SidebarLogs.Width = _sidebarWidth;
                SidebarLogs.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseSidebar_Click(object sender, RoutedEventArgs e)
        {
            SidebarLogs.Visibility = Visibility.Collapsed;
            _currentLogTask = null;
            TxtSidebarLog.DataContext = null;
        }

        private void BtnViewTaskAssets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutomationTask task)
            {
                string outputDir = task.OutputDir;
                if (!Directory.Exists(outputDir))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDir);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to create output folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                System.Diagnostics.Process.Start("explorer.exe", outputDir);
            }
        }

        private async void BtnDeleteTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutomationTask task)
            {
                var result = MessageBox.Show($"Are you sure you want to delete this task (Video ID: {task.VideoId}) and all of its assets?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    Tasks.Remove(task);
                    
                    // Delete assets in background
                    _ = Task.Run(async () =>
                    {
                        if (Directory.Exists(task.OutputDir))
                        {
                            try
                            {
                                Directory.Delete(task.OutputDir, true);
                            }
                            catch (Exception ex)
                            {
                                Log($"[WARNING] Failed to delete output folder: {ex.Message}");
                            }
                        }
                        await DeleteTaskFromHistoryAsync(task);
                    });
                }
            }
        }
        // Sidebar drag handle events for resizable drawer
        private void SidebarDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSidebar = true;
            _dragStartX = e.GetPosition(this).X;
            ((FrameworkElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void SidebarDragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingSidebar) return;

            double currentX = e.GetPosition(this).X;
            double delta = _dragStartX - currentX; // moving left = positive delta = wider sidebar
            double newWidth = _sidebarWidth + delta;

            // Clamp between 280 and 80% of window width
            double maxWidth = this.ActualWidth * 0.8;
            newWidth = Math.Max(280, Math.Min(newWidth, maxWidth));

            SidebarLogs.Width = newWidth;
        }

        private void BtnBrowseVoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutomationTask task)
            {
                string apiKey = ConfigService.CurrentSettings.Ai84ApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Please enter your AI84 API Key first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selector = new VoiceSelectorWindow(apiKey, task.VoiceId)
                {
                    Owner = this
                };

                if (selector.ShowDialog() == true)
                {
                    task.VoiceId = selector.SelectedVoiceId;
                    if (selector.SelectedVoice != null)
                    {
                        task.TargetLanguage = LanguageHelper.FormatLanguage(selector.SelectedVoice.language);
                    }
                }
            }
        }

        private void SidebarDragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDraggingSidebar) return;
            _isDraggingSidebar = false;
            _sidebarWidth = SidebarLogs.Width;
            ((FrameworkElement)sender).ReleaseMouseCapture();
            e.Handled = true;
        }
    }
}
