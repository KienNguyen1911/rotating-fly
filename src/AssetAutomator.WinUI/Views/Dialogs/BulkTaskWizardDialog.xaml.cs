using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.Core.Models;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class TopicItem : INotifyPropertyChanged
{
    private string _topic = "";
    public string Topic
    {
        get => _topic;
        set
        {
            if (_topic != value)
            {
                _topic = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Topic)));
            }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class BulkTaskWizardDialog : ContentDialog
{
    private bool _isStep2 = false;

    public ObservableCollection<TopicItem> Topics { get; } = new();

    public GeminiTaskModel TemplateTask { get; }

    public ObservableCollection<GemOptionItem> AvailableScriptwriterGems { get; }
    public ObservableCollection<GemOptionItem> AvailableSceneCreatorGems { get; }
    public ObservableCollection<string> AvailableAiModels { get; }
    public ObservableCollection<string> AvailableImageProviders { get; }

    public BulkTaskWizardDialog(
        GeminiTaskModel templateTask,
        ObservableCollection<GemOptionItem> scriptwriterGems,
        ObservableCollection<GemOptionItem> sceneCreatorGems,
        ObservableCollection<string> aiModels,
        ObservableCollection<string> imageProviders)
    {
        this.InitializeComponent();

        TemplateTask = templateTask;
        AvailableScriptwriterGems = scriptwriterGems;
        AvailableSceneCreatorGems = sceneCreatorGems;
        AvailableAiModels = aiModels;
        AvailableImageProviders = imageProviders;

        // Add 1 default row
        Topics.Add(new TopicItem());
    }

    private void AddTopic_Click(object sender, RoutedEventArgs e)
    {
        Topics.Add(new TopicItem());
    }

    private void RemoveTopic_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TopicItem item)
        {
            Topics.Remove(item);
        }
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_isStep2)
        {
            // Move to Step 2
            args.Cancel = true; // Prevent closing
            _isStep2 = true;
            Step1Panel.Visibility = Visibility.Collapsed;
            Step2Panel.Visibility = Visibility.Visible;
            
            PrimaryButtonText = "Tạo Task";
            SecondaryButtonText = "Quay lại";
        }
        else
        {
            // Clean up empty topics before closing
            var emptyTopics = Topics.Where(t => string.IsNullOrWhiteSpace(t.Topic)).ToList();
            foreach (var t in emptyTopics)
            {
                Topics.Remove(t);
            }

            if (Topics.Count == 0)
            {
                // Force user to add at least 1 topic
                args.Cancel = true;
                _isStep2 = false;
                Step1Panel.Visibility = Visibility.Visible;
                Step2Panel.Visibility = Visibility.Collapsed;
                PrimaryButtonText = "Tiếp tục";
                SecondaryButtonText = "Hủy bỏ";
            }
        }
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isStep2)
        {
            // Go back to Step 1
            args.Cancel = true; // Prevent closing
            _isStep2 = false;
            Step1Panel.Visibility = Visibility.Visible;
            Step2Panel.Visibility = Visibility.Collapsed;
            
            PrimaryButtonText = "Tiếp tục";
            SecondaryButtonText = "Hủy bỏ";
        }
    }
}
