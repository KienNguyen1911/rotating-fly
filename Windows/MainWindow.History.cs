using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AssetAutomator
{
    /// <summary>
    /// History tab UI handlers. All persistence logic delegated to HistoryService.
    /// Reduced from ~220 lines to ~60 lines.
    /// </summary>
    public partial class MainWindow : Window
    {
        private void LoadHistoryDates()
        {
            HistoryDates.Clear();
            var dates = _historyService.LoadHistoryDates();
            foreach (var date in dates)
            {
                HistoryDates.Add(date);
            }
        }

        private void LoadHistoryTasksForDate(string date)
        {
            HistoryTasks.Clear();
            var tasks = _historyService.LoadHistoryTasksForDate(date);
            foreach (var task in tasks)
            {
                HistoryTasks.Add(task);
            }
            ApplyHistoryFilters();
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
                if (System.IO.Directory.Exists(outputDir))
                {
                    System.Diagnostics.Process.Start("explorer.exe", outputDir);
                }
                else
                {
                    MessageBox.Show("Output folder does not exist for this task.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }
}
