using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class ProxyResultModel
{
    public string Proxy { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public string Status => IsOnline ? "ONLINE" : "OFFLINE";
    public string Response { get; set; } = string.Empty;
}

public sealed partial class ProxyTestResultDialog : ContentDialog
{
    public ProxyTestResultDialog(List<ProxyResultModel> results, int onlineCount, int offlineCount)
    {
        InitializeComponent();
        LstResults.ItemsSource = results;
        TxtSummary.Text = $"Tổng số: {results.Count} proxies | ONLINE: {onlineCount} | OFFLINE: {offlineCount}";
    }
}
