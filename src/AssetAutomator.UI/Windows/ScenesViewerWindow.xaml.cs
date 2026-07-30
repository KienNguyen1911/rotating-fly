using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AssetAutomator.Core.Models;

namespace AssetAutomator.UI.Windows
{
    /// <summary>
    /// Interaction logic for ScenesViewerWindow.xaml
    /// Popup window displaying detailed table of parsed scenes from scenes.json.
    /// </summary>
    public partial class ScenesViewerWindow : Window
    {
        public bool ImportRequested { get; private set; } = false;
        public List<SceneItemModel> ScenesList { get; private set; } = new List<SceneItemModel>();

        public ScenesViewerWindow(ScenesJsonRootModel rootData)
        {
            InitializeComponent();

            if (rootData != null)
            {
                TxtVideoTitle.Text = string.IsNullOrWhiteSpace(rootData.video_title) ? "Scenes Overview" : rootData.video_title;
                TxtSceneStats.Text = $"Total scenes: {rootData.scenes?.Count ?? 0}";

                if (rootData.scenes != null)
                {
                    foreach (var entry in rootData.scenes)
                    {
                        ScenesList.Add(new SceneItemModel
                        {
                            SceneNumber = entry.scene,
                            Id = entry.id,
                            StartTime = entry.time?.start ?? string.Empty,
                            EndTime = entry.time?.end ?? string.Empty,
                            Duration = entry.time?.duration ?? 0,
                            Transcript = entry.transcript,
                            ImagePrompt = entry.image_prompt
                        });
                    }
                }

                DgridScenes.ItemsSource = ScenesList;
            }
        }

        private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SceneItemModel scene && !string.IsNullOrEmpty(scene.ImagePrompt))
            {
                Clipboard.SetText(scene.ImagePrompt);
                MessageBox.Show($"Copied Scene #{scene.SceneNumber} prompt to clipboard!", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnApplyAllToBatch_Click(object sender, RoutedEventArgs e)
        {
            ImportRequested = true;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
