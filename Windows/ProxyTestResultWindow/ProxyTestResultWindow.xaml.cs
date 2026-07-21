using System;
using System.Collections.Generic;
using System.Windows;

namespace AssetAutomator
{
    /// <summary>
    /// Interaction logic for ProxyTestResultWindow.xaml
    /// </summary>
    public partial class ProxyTestResultWindow : Window
    {
        public ProxyTestResultWindow(List<ProxyTestResultItem> results, int onlineCount, int offlineCount)
        {
            InitializeComponent();
            GridResults.ItemsSource = results;
            TxtSummary.Text = $"Tổng số: {results.Count} proxies | ONLINE: {onlineCount} | OFFLINE: {offlineCount}";
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class ProxyTestResultItem
    {
        public string Proxy { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "ONLINE" or "OFFLINE"
        public string Response { get; set; } = string.Empty;
        public bool IsOnline => Status == "ONLINE";
    }
}
