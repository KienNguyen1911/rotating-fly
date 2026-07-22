using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.ComponentModel;

namespace AssetAutomator
{
    /// <summary>
    /// Task tab UI handlers. History persistence delegated to HistoryService.
    /// </summary>
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
                if (e.PropertyName == nameof(AutomationTask.Logs))
                {
                    return;
                }
                // Removed: _ = Task.Run(() => _historyService.SaveTaskToHistoryAsync(task));
            }
        }

        private static Brush? GetLogLineBrush(string text)
        {
            if (text.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || text.Contains("failed", StringComparison.OrdinalIgnoreCase) || text.Contains("Exception", StringComparison.OrdinalIgnoreCase) || text.Contains("ERR_"))
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
            }
            if (text.Contains("[INFO]", StringComparison.OrdinalIgnoreCase) || text.Contains("[SUCCESS]", StringComparison.OrdinalIgnoreCase) || text.Contains("Success", StringComparison.OrdinalIgnoreCase) || text.Contains("Saved ", StringComparison.OrdinalIgnoreCase) || text.Contains("[ALIVE]", StringComparison.OrdinalIgnoreCase) || text.Contains("completed successfully", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Emerald Green
            }
            if (text.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase) || text.Contains("[FALLBACK]", StringComparison.OrdinalIgnoreCase) || text.Contains("Retrying", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")); // Amber Orange
            }
            if (text.Contains("[STEP", StringComparison.OrdinalIgnoreCase) || text.Contains("[FLOW]", StringComparison.OrdinalIgnoreCase) || text.Contains("[SCRIPT-BRANCH]", StringComparison.OrdinalIgnoreCase) || text.Contains("[IMAGE-BRANCH]", StringComparison.OrdinalIgnoreCase) || text.Contains("[PROXY]", StringComparison.OrdinalIgnoreCase) || text.Contains("[POOL]", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")); // Sky Blue / Cyan
            }
            return null; // Return null to inherit DynamicResource InkBrush (White in Dark mode, Black in Light mode)
        }

        private void AppendLogToRichTextBox(string logLine)
        {
            if (TxtSidebarLog.Document == null)
            {
                TxtSidebarLog.Document = new FlowDocument();
            }
            var brush = GetLogLineBrush(logLine);
            var paragraph = new Paragraph(new Run(logLine.TrimEnd('\r', '\n')))
            {
                Margin = new Thickness(0, 1, 0, 1)
            };
            if (brush != null)
            {
                paragraph.Foreground = brush;
            }
            TxtSidebarLog.Document.Blocks.Add(paragraph);
            TxtSidebarLog.ScrollToEnd();
        }

        public void SetLogsToRichTextBox(string fullLogs)
        {
            var doc = new FlowDocument();
            if (!string.IsNullOrEmpty(fullLogs))
            {
                var lines = fullLogs.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var brush = GetLogLineBrush(line);
                    var paragraph = new Paragraph(new Run(line))
                    {
                        Margin = new Thickness(0, 1, 0, 1)
                    };
                    if (brush != null)
                    {
                        paragraph.Foreground = brush;
                    }
                    doc.Blocks.Add(paragraph);
                }
            }
            TxtSidebarLog.Document = doc;
            TxtSidebarLog.ScrollToEnd();
        }

        private void LogTask(AutomationTask task, string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            task.Logs += formattedMessage;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SidebarLogs.Visibility == Visibility.Visible && TxtSidebarLog.DataContext == task)
                {
                    AppendLogToRichTextBox(formattedMessage);
                }
            }));
        }

        private void BtnAddTask_Click(object sender, RoutedEventArgs e)
        {
            string defaultProf = ConfigService.CurrentSettings.DefaultChromeProfile;
            string selectedProfile = !string.IsNullOrEmpty(defaultProf) && ProfileList.Contains(defaultProf)
                ? defaultProf
                : (ProfileList.Count > 0 ? ProfileList[0] : string.Empty);

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
                string defaultProf = ConfigService.CurrentSettings.DefaultChromeProfile;
                string selectedProfile = !string.IsNullOrEmpty(defaultProf) && ProfileList.Contains(defaultProf)
                    ? defaultProf
                    : (ProfileList.Count > 0 ? ProfileList[0] : string.Empty);
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

                bool runStep1 = ChkStepDownloadThumbnail.IsChecked == true;
                bool runStep2 = ChkStepGetTranscript.IsChecked == true;
                bool runStep3 = ChkStepRewrittenTranscript.IsChecked == true;
                bool runStep4 = ChkStepVoiceover.IsChecked == true;
                bool runStep5 = ChkStepGenerateThumbnail.IsChecked == true;
                bool runStepSrt = ChkStepSrt.IsChecked == true;

                if (!runStep1 && !runStep2 && !runStep3 && !runStep4 && !runStep5 && !runStepSrt)
                {
                    MessageBox.Show("Please select at least one feature/step to run.", "No Feature Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Map global checkboxes to task
                task.Step1 = runStep1;
                task.Step2 = runStep2;
                task.Step3 = runStep3;
                task.Step4 = runStep4;
                task.Step5 = runStep5;
                task.StepSrt = runStepSrt;

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
                        finally
                        {
                            await _historyService.SaveTaskToHistoryAsync(task);
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
            var selectedTasks = Tasks.Where(t => t.IsSelected).ToArray();
            if (selectedTasks.Length == 0)
            {
                MessageBox.Show("No selected tasks to run. Please check the 'Chọn' column.", "No Tasks Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var task in selectedTasks)
            {
                if (string.IsNullOrEmpty(task.SelectedProfile))
                {
                    MessageBox.Show($"Please select a Chrome Profile for the task with Video ID: {task.VideoId}.", "Profile Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            bool runStep1 = ChkStepDownloadThumbnail.IsChecked == true;
            bool runStep2 = ChkStepGetTranscript.IsChecked == true;
            bool runStep3 = ChkStepRewrittenTranscript.IsChecked == true;
            bool runStep4 = ChkStepVoiceover.IsChecked == true;
            bool runStep5 = ChkStepGenerateThumbnail.IsChecked == true;
            bool runStepSrt = ChkStepSrt.IsChecked == true;

            if (!runStep1 && !runStep2 && !runStep3 && !runStep4 && !runStep5 && !runStepSrt)
            {
                MessageBox.Show("Please select at least one feature/step to run.", "No Feature Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var task in selectedTasks)
            {
                // Map global checkboxes to task
                task.Step1 = runStep1;
                task.Step2 = runStep2;
                task.Step3 = runStep3;
                task.Step4 = runStep4;
                task.Step5 = runStep5;
                task.StepSrt = runStepSrt;
            }

            SaveApplicationSettings();
            BtnRun.IsEnabled = false;
            BtnAddTask.IsEnabled = false;
            try
            {
                int maxConcurrent = ConfigService.CurrentSettings.MaxConcurrentTasks;
                if (maxConcurrent <= 0) maxConcurrent = 4;
                Log($"[FLOW] Starting batch execution for {selectedTasks.Length} tasks (Max {maxConcurrent} concurrent threads)...");

                await Task.Run(async () =>
                {
                    using var concurrencySemaphore = new System.Threading.SemaphoreSlim(maxConcurrent, maxConcurrent);
                    var tasks = selectedTasks.Select(async task =>
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
                            await _historyService.SaveTaskToHistoryAsync(task);
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
                SetLogsToRichTextBox(task.Logs ?? string.Empty);
                SidebarLogs.Width = _sidebarWidth;
                SidebarLogs.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseSidebar_Click(object sender, RoutedEventArgs e)
        {
            SidebarLogs.Visibility = Visibility.Collapsed;
            _currentLogTask = null;
            TxtSidebarLog.DataContext = null;
            TxtSidebarLog.Document = new FlowDocument();
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
                        await _historyService.DeleteTaskFromHistoryAsync(task);
                    });
                }
            }
        }

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
            double delta = _dragStartX - currentX;
            double newWidth = _sidebarWidth + delta;

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
