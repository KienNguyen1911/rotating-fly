using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AssetAutomator.UI.Windows
{
    public partial class CookieInputWindow : Window
    {
        public string CookieInput { get; private set; } = string.Empty;
        public bool IsConfirmed { get; private set; }

        public CookieInputWindow(string? prefilledText = null)
        {
            InitializeComponent();

            if (!string.IsNullOrWhiteSpace(prefilledText))
            {
                TxtCookiesInput.Text = prefilledText;
            }

            UpdateCharCount();
            UpdatePlaceholder();

            TxtCookiesInput.TextChanged += (_, _) =>
            {
                UpdateCharCount();
                UpdatePlaceholder();
            };
        }

        private void UpdateCharCount()
        {
            RunCharCount.Text = TxtCookiesInput.Text.Length.ToString();
        }

        private void UpdatePlaceholder()
        {
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtCookiesInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            string text = TxtCookiesInput.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                ShowStatus("⚠️ Chưa nhập cookies", "Vui lòng dán cookies vào ô bên trên trước khi nhấn Nhập.", "#F59E0B");
                return;
            }

            CookieInput = text;
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        private void ShowStatus(string header, string details, string colorHex)
        {
            BorderStatus.Visibility = Visibility.Visible;
            TxtStatusHeader.Text = header;
            TxtStatusHeader.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            TxtStatusDetails.Text = details;
        }
    }
}
