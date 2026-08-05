using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class UpdateDialog : ContentDialog
{
    private static readonly HttpClient _httpClient = new HttpClient();

    static UpdateDialog()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AssetAutomator-App");
    }

    public UpdateDialog(string installedVersion, string latestVersion, string? changelogUrl = null)
    {
        InitializeComponent();
        TxtCurrentVersion.Text = $"v{installedVersion}";
        TxtNewVersion.Text = $"v{latestVersion}";
        _ = FetchReleaseNotesAsync(latestVersion, changelogUrl);
    }

    private async Task FetchReleaseNotesAsync(string version, string? changelogUrl)
    {
        try
        {
            string tag = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}";
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
        catch { }

        if (!string.IsNullOrWhiteSpace(changelogUrl))
        {
            TxtReleaseNotes.Text = $"Xem chi tiết tại:\n{changelogUrl}";
        }
        else
        {
            TxtReleaseNotes.Text = "Phiên bản mới bao gồm nhiều tính năng, cải tiến hiệu năng và sửa lỗi hệ thống.";
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Open releases page in browser
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/KienNguyen1911/AssetAutomator-Releases/releases",
                UseShellExecute = true
            });
        }
        catch { }
    }
}
