using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using AssetAutomator.Core.Models;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class SceneItemModel
{
    public string SceneIndex { get; set; } = string.Empty;
    public string ScriptLine { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}

public sealed partial class ScenesViewerDialog : ContentDialog
{
    public bool ImportRequested { get; private set; }
    public List<SceneItemModel> ScenesList { get; private set; } = new();

    public ScenesViewerDialog(ScenesJsonRootModel rootData)
    {
        InitializeComponent();

        if (rootData != null)
        {
            string title = string.IsNullOrWhiteSpace(rootData.video_title) ? "Scenes Overview" : rootData.video_title;
            TxtInfo.Text = title;
            int count = rootData.scenes?.Count ?? 0;
            TxtSceneStats.Text = $"Tổng số phân cảnh: {count} | Sẵn sàng xuất prompt sang Batch Image Generator";

            if (rootData.scenes != null)
            {
                foreach (var entry in rootData.scenes)
                {
                    ScenesList.Add(new SceneItemModel
                    {
                        SceneIndex = $"Cảnh #{entry.scene}",
                        ScriptLine = entry.transcript ?? string.Empty,
                        Prompt = entry.image_prompt ?? string.Empty
                    });
                }
            }

            LstScenes.ItemsSource = ScenesList;
        }

        BtnApplyAllToBatch.Click += BtnApplyAllToBatch_Click;
    }

    private void BtnApplyAllToBatch_Click(object sender, RoutedEventArgs e)
    {
        ImportRequested = true;
        Hide();
    }

    private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SceneItemModel scene && !string.IsNullOrEmpty(scene.Prompt))
        {
            var package = new DataPackage();
            package.SetText(scene.Prompt);
            Clipboard.SetContent(package);
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ImportRequested = true;
    }
}
