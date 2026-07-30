using System;
using System.Collections.Generic;
using System.Windows;

namespace AssetAutomator.UI.Windows
{
    public partial class BulkTaskWindow : Window
    {
        public List<(string Url, string VoiceId)> TasksToCreate { get; private set; } = new();

        public BulkTaskWindow()
        {
            InitializeComponent();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtBulkInput.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Vui lòng nhập danh sách link trước.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            TasksToCreate.Clear();

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var parts = trimmed.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string url = parts[0];
                    string voiceId = parts.Length > 1 ? parts[1] : string.Empty;
                    TasksToCreate.Add((url, voiceId));
                }
            }

            if (TasksToCreate.Count == 0)
            {
                MessageBox.Show("Không tìm thấy dòng hợp lệ nào để tạo task.", "Lỗi nhập liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
