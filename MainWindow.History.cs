using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCreateImage
{
    public partial class MainWindow : Window
    {
        private static readonly SemaphoreSlim _historyFileSemaphore = new SemaphoreSlim(1, 1);

        private async Task SaveTaskToHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                string historyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
                if (!Directory.Exists(historyDir))
                {
                    Directory.CreateDirectory(historyDir);
                }

                string dateStr = task.CreatedAt.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(historyDir, $"{dateStr}.json");

                var list = new System.Collections.Generic.List<HistoryTaskModel>();
                if (File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = await File.ReadAllTextAsync(filePath);
                        var existingList = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HistoryTaskModel>>(existingJson);
                        if (existingList != null)
                        {
                            list = existingList;
                        }
                    }
                    catch {}
                }

                var existing = list.FirstOrDefault(t => t.Id == task.Id);
                if (existing != null)
                {
                    existing.VideoUrl = task.VideoUrl;
                    existing.TargetLanguage = task.TargetLanguage;
                    existing.VoiceId = task.VoiceId;
                    existing.Step1 = task.Step1;
                    existing.Step2 = task.Step2;
                    existing.Step3 = task.Step3;
                    existing.Step4 = task.Step4;
                    existing.Step5 = task.Step5;
                    existing.Status = task.Status;
                    existing.SelectedProfile = task.SelectedProfile;
                    existing.Logs = task.Logs;
                }
                else
                {
                    list.Add(new HistoryTaskModel
                    {
                        Id = task.Id,
                        VideoUrl = task.VideoUrl,
                        TargetLanguage = task.TargetLanguage,
                        VoiceId = task.VoiceId,
                        Step1 = task.Step1,
                        Step2 = task.Step2,
                        Step3 = task.Step3,
                        Step4 = task.Step4,
                        Step5 = task.Step5,
                        Status = task.Status,
                        SelectedProfile = task.SelectedProfile,
                        Logs = task.Logs,
                        CreatedAt = task.CreatedAt
                    });
                }

                string newJson = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, newJson);
            }
            catch (Exception ex)
            {
                Log($"[ERROR] SaveTaskToHistory failed: {ex.Message}");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }

        private void LoadHistoryDates()
        {
            try
            {
                string historyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
                HistoryDates.Clear();
                if (Directory.Exists(historyDir))
                {
                    var files = Directory.GetFiles(historyDir, "*.json");
                    var dates = files.Select(Path.GetFileNameWithoutExtension)
                                     .OrderByDescending(d => d);
                    foreach (var date in dates)
                    {
                        if (date != null) HistoryDates.Add(date);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load history dates: {ex.Message}");
            }
        }

        private void LoadHistoryTasksForDate(string date)
        {
            try
            {
                HistoryTasks.Clear();
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history", $"{date}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HistoryTaskModel>>(json);
                    if (list != null)
                    {
                        foreach (var task in list)
                        {
                            HistoryTasks.Add(task);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] Failed to load history tasks for {date}: {ex.Message}");
            }
        }

        private void LboxHistoryDates_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LboxHistoryDates.SelectedItem is string selectedDate)
            {
                LoadHistoryTasksForDate(selectedDate);
            }
        }

        private void BtnRefreshHistory_Click(object sender, RoutedEventArgs e)
        {
            LoadHistoryDates();
            if (LboxHistoryDates.SelectedItem is string selectedDate)
            {
                LoadHistoryTasksForDate(selectedDate);
            }
            else
            {
                HistoryTasks.Clear();
            }
        }

        private void BtnViewHistoryTaskLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is HistoryTaskModel task)
            {
                TxtSidebarLog.DataContext = task;
                TxtSidebarLog.Text = task.Logs;
                TxtSidebarLog.ScrollToEnd();
                SidebarLogs.Visibility = Visibility.Visible;
            }
        }

        private void BtnViewHistoryTaskAssets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is HistoryTaskModel task)
            {
                string outputDir = task.OutputDir;
                if (Directory.Exists(outputDir))
                {
                    System.Diagnostics.Process.Start("explorer.exe", outputDir);
                }
                else
                {
                    MessageBox.Show("Output folder does not exist for this task.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async Task DeleteTaskFromHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                string historyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
                string dateStr = task.CreatedAt.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(historyDir, $"{dateStr}.json");

                if (File.Exists(filePath))
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<HistoryTaskModel>>(json);
                    if (list != null)
                    {
                        var updatedList = list.Where(t => t.Id != task.Id).ToList();
                        string newJson = System.Text.Json.JsonSerializer.Serialize(updatedList, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(filePath, newJson);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[ERROR] DeleteTaskFromHistory failed: {ex.Message}");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }
    }
}
