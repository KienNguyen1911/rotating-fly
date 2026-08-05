using System;
using Microsoft.UI.Xaml.Controls;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class NewProjectDialog : ContentDialog
{
    public string ProjectName { get; private set; } = string.Empty;

    public NewProjectDialog(string defaultName = "")
    {
        InitializeComponent();
        TxtProjectName.Text = !string.IsNullOrWhiteSpace(defaultName)
            ? defaultName
            : $"Project_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        string name = TxtProjectName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            args.Cancel = true;
            return;
        }
        ProjectName = name;
    }
}
