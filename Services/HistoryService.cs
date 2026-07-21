using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AssetAutomator
{
    /// <summary>
    /// Manages task history persistence (save/load/delete) to JSON files organized by date.
    /// Extracted from MainWindow.History.cs to follow SRP.
    /// </summary>
    public class HistoryService
    {
        private static readonly SemaphoreSlim _historyFileSemaphore = new SemaphoreSlim(1, 1);
        private readonly Action<string> _log;

        public HistoryService(Action<string> log)
        {
            _log = log;
        }

        private string GetHistoryDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
        }

        /// <summary>
        /// Saves or updates a task entry in the history file for its creation date.
        /// Thread-safe via semaphore.
        /// </summary>
        public async Task SaveTaskToHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                string historyDir = GetHistoryDir();
                if (!Directory.Exists(historyDir))
                {
                    Directory.CreateDirectory(historyDir);
                }

                string dateStr = task.CreatedAt.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(historyDir, $"{dateStr}.json");

                var list = new List<HistoryTaskModel>();
                if (File.Exists(filePath))
                {
                    try
                    {
                        string existingJson = await File.ReadAllTextAsync(filePath);
                        var existingList = System.Text.Json.JsonSerializer.Deserialize<List<HistoryTaskModel>>(existingJson);
                        if (existingList != null)
                        {
                            list = existingList;
                        }
                    }
                    catch { }
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
                    existing.StepSrt = task.StepSrt;
                    existing.Status = task.Status;
                    existing.SelectedProfile = task.SelectedProfile;
                    existing.Logs = task.Logs;
                    existing.Step1Status = task.Step1Status;
                    existing.Step2Status = task.Step2Status;
                    existing.Step3Status = task.Step3Status;
                    existing.Step4Status = task.Step4Status;
                    existing.Step5Status = task.Step5Status;
                    existing.StepSrtStatus = task.StepSrtStatus;
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
                        StepSrt = task.StepSrt,
                        Status = task.Status,
                        SelectedProfile = task.SelectedProfile,
                        Logs = task.Logs,
                        CreatedAt = task.CreatedAt,
                        Step1Status = task.Step1Status,
                        Step2Status = task.Step2Status,
                        Step3Status = task.Step3Status,
                        Step4Status = task.Step4Status,
                        Step5Status = task.Step5Status,
                        StepSrtStatus = task.StepSrtStatus
                    });
                }

                string newJson = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(filePath, newJson);
            }
            catch (Exception ex)
            {
                _log($"[ERROR] SaveTaskToHistory failed: {ex.Message}");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }

        /// <summary>
        /// Deletes a task from its history date file. Thread-safe via semaphore.
        /// </summary>
        public async Task DeleteTaskFromHistoryAsync(AutomationTask task)
        {
            await _historyFileSemaphore.WaitAsync();
            try
            {
                string historyDir = GetHistoryDir();
                string dateStr = task.CreatedAt.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(historyDir, $"{dateStr}.json");

                if (File.Exists(filePath))
                {
                    string json = await File.ReadAllTextAsync(filePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<HistoryTaskModel>>(json);
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
                _log($"[ERROR] DeleteTaskFromHistory failed: {ex.Message}");
            }
            finally
            {
                _historyFileSemaphore.Release();
            }
        }

        /// <summary>
        /// Returns a list of date strings (yyyy-MM-dd) for which history files exist, ordered descending.
        /// </summary>
        public List<string> LoadHistoryDates()
        {
            var dates = new List<string>();
            try
            {
                string historyDir = GetHistoryDir();
                if (Directory.Exists(historyDir))
                {
                    var files = Directory.GetFiles(historyDir, "*.json");
                    dates = files.Select(Path.GetFileNameWithoutExtension)
                                 .Where(d => d != null)
                                 .Select(d => d!)
                                 .OrderByDescending(d => d)
                                 .ToList();
                }
            }
            catch (Exception ex)
            {
                _log($"[ERROR] Failed to load history dates: {ex.Message}");
            }
            return dates;
        }

        /// <summary>
        /// Loads all history tasks for a specific date.
        /// </summary>
        public List<HistoryTaskModel> LoadHistoryTasksForDate(string date)
        {
            var tasks = new List<HistoryTaskModel>();
            try
            {
                string filePath = Path.Combine(GetHistoryDir(), $"{date}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<HistoryTaskModel>>(json);
                    if (list != null)
                    {
                        tasks = list;
                    }
                }
            }
            catch (Exception ex)
            {
                _log($"[ERROR] Failed to load history tasks for {date}: {ex.Message}");
            }
            return tasks;
        }
    }
}
