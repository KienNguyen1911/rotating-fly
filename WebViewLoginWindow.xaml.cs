using System;
using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AutoCreateImage
{
    public partial class WebViewLoginWindow : Window
    {
        private readonly string _sessionId;
        private readonly string _authorizeUrl;
        private readonly string _redirectUriPrefix;

        public string CallbackUrl { get; private set; } = string.Empty;

        public WebViewLoginWindow(string sessionId, string authorizeUrl, string redirectUriPrefix)
        {
            InitializeComponent();
            _sessionId = sessionId;
            _authorizeUrl = authorizeUrl;
            _redirectUriPrefix = redirectUriPrefix;

            Loaded += WebViewLoginWindow_Loaded;
        }

        private async void WebViewLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatus.Text = "Đang khởi động trình duyệt bảo mật...";
                // Initialize WebView2 environment
                await WvBrowser.EnsureCoreWebView2Async(null);
                
                // Disable browser context menu to look cleaner
                WvBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                TxtStatus.Text = "Đang tải trang đăng nhập ChatGPT...";
                WvBrowser.Source = new Uri(_authorizeUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo trình duyệt: {ex.Message}\nHãy chắc chắn bạn đã cài đặt Microsoft Edge WebView2 Runtime.", 
                                "Lỗi khởi tạo", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private void WvBrowser_SourceChanged(object sender, CoreWebView2SourceChangedEventArgs e)
        {
            string currentUrl = WvBrowser.Source?.ToString() ?? string.Empty;
            Debug.WriteLine($"[WebView URL]: {currentUrl}");

            if (!string.IsNullOrEmpty(currentUrl))
            {
                TxtStatus.Text = currentUrl;

                // Check if we hit the redirect callback url
                if (currentUrl.StartsWith(_redirectUriPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    CallbackUrl = currentUrl;
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WvBrowser.Reload();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lỗi reload: {ex.Message}");
            }
        }
    }
}
