using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class BulkTaskDialog : ContentDialog
{
    public List<(string Url, string VoiceId)> TasksToCreate { get; private set; } = new();

    public BulkTaskDialog()
    {
        InitializeComponent();
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string text = TxtBulkInput.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            args.Cancel = true;
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
            args.Cancel = true;
        }
    }
}
