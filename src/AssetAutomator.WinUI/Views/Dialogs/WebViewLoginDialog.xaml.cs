using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class WebViewLoginDialog : ContentDialog
{
    private readonly string _authorizeUrl;
    private readonly string _redirectUriPrefix;

    public string CallbackUrl { get; private set; } = string.Empty;
    public bool Success { get; private set; }

    public WebViewLoginDialog(string authorizeUrl, string redirectUriPrefix)
    {
        InitializeComponent();
        _authorizeUrl = authorizeUrl;
        _redirectUriPrefix = redirectUriPrefix;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        TxtStatus.Text = $"URL: {_authorizeUrl}";
    }
}
