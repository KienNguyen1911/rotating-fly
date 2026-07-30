using System.Collections.Generic;
using System.Windows;
using AssetAutomator.UI.Models;

namespace AssetAutomator.UI.Windows
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
}
