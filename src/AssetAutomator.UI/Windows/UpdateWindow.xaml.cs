using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using AutoUpdaterDotNET;

namespace AssetAutomator.UI.Windows
{
    /// <summary>
    /// Interaction logic for UpdateWindow.xaml
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private readonly UpdateInfoEventArgs _args;
        private static readonly HttpClient _httpClient = new HttpClient();

        static UpdateWindow()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AssetAutomator-App");
        }

        public UpdateWindow(UpdateInfoEventArgs args)
        {
            InitializeComponent();
            _args = args ?? throw new ArgumentNullException(nameof(args));

            TxtCurrentVersion.Text = $"v{_args.InstalledVersion}";
            TxtNewVersion.Text = $"v{_args.CurrentVersion}";
            TxtHeaderTitle.Text = $"AssetAutomator v{_args.CurrentVersion} đã sẵn sàng!";

            Loaded += UpdateWindow_Loaded;
        }

        private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await FetchReleaseNotesAsync();
        }

        private async Task FetchReleaseNotesAsync()
        {
            try
            {
                string tag = _args.CurrentVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? _args.CurrentVersion
                    : $"v{_args.CurrentVersion}";

                string apiUrl = $"https://api.github.com/repos/KienNguyen1911/AssetAutomator-Releases/releases/tags/{tag}";
                var response = await _httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    apiUrl = "https://api.github.com/repos/KienNguyen1911/AssetAutomator-Releases/releases/latest";
                    response = await _httpClient.GetAsync(apiUrl);
                }

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("body", out var bodyProp))
                    {
                        string body = bodyProp.GetString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            TxtReleaseNotes.Text = body.Trim();
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Fallback on exception
            }

            if (!string.IsNullOrWhiteSpace(_args.ChangelogURL))
            {
                TxtReleaseNotes.Text = $"Xem thông tin cập nhật chi tiết tại:\n{_args.ChangelogURL}";
            }
            else
            {
                TxtReleaseNotes.Text = "Phiên bản mới bao gồm nhiều tính năng, cải tiến hiệu năng và sửa lỗi hệ thống.";
            }
        }

        private void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AutoUpdater.DownloadUpdate(_args))
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể bắt đầu tải bản cập nhật: {ex.Message}", "Lỗi Cập Nhật", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemindLater_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnSkipVersion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_args.CurrentVersion))
                {
                    AutoUpdater.PersistenceProvider.SetSkippedVersion(new Version(_args.CurrentVersion));
                }
            }
            catch
            {
                // Ignore parsing errors for version string if any
            }
            Close();
        }
    }
}
