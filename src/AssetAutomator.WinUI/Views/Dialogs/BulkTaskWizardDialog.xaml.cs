using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AssetAutomator.Core.Models;

namespace AssetAutomator.WinUI.Views.Dialogs;

public class PreviewItem : INotifyPropertyChanged
{
    private string _displayText = "";
    public string DisplayText
    {
        get => _displayText;
        set { _displayText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayText))); }
    }
    public bool IsYouTubeUrl { get; set; }
    public string RawInput { get; set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class BulkTaskWizardDialog : ContentDialog
{
    private static readonly Regex YoutubeRegex = new(
        @"^(https?://)?(www\.)?(youtube\.com/watch\?v=|youtu\.be/|youtube\.com/shorts/)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ObservableCollection<GeminiTaskModel> AvailableTemplates { get; } = new();
    public ObservableCollection<PreviewItem> PreviewItems { get; } = new();

    [Browsable(false)]
    public GeminiTaskModel? SelectedTemplate
    {
        get => _selectedTemplate;
        set
        {
            _selectedTemplate = value;
            OnPropertyChanged(nameof(SelectedTemplate));
        }
    }

    private GeminiTaskModel? _selectedTemplate;

    [Browsable(false)]
    public int TaskCount => PreviewItems.Count;

    [Browsable(false)]
    public bool HasTopics => PreviewItems.Count > 0;

    [Browsable(false)]
    public string CreateButtonText => HasTopics
        ? $"Tạo {TaskCount} Task"
        : "Tạo Task";

    public BulkTaskWizardDialog(IEnumerable<GeminiTaskModel> existingTasks)
    {
        InitializeComponent();

        // Populate templates from existing tasks
        foreach (var task in existingTasks.Take(20))
        {
            AvailableTemplates.Add(task);
        }

        // Select first template by default
        if (AvailableTemplates.Count > 0)
        {
            SelectedTemplate = AvailableTemplates[0];
        }

        // Subscribe to template selection changes
        TemplateComboBox.SelectionChanged += (s, e) =>
        {
            if (TemplateComboBox.SelectedItem is GeminiTaskModel template)
            {
                SelectedTemplate = template;
            }
        };

        // Update button text when dialog loads
        this.Loaded += (s, e) => UpdatePrimaryButtonText();
    }

    private void UpdatePrimaryButtonText()
    {
        var count = PreviewItems.Count;
        this.PrimaryButtonText = count > 0 ? $"Tạo {count} Task" : "Tạo Task";
    }

    private void TopicsInputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
        UpdateButtonText();
    }

    private void UpdatePreview()
    {
        PreviewItems.Clear();

        if (string.IsNullOrWhiteSpace(TopicsInputBox?.Text))
        {
            OnPropertyChanged(nameof(TaskCount));
            OnPropertyChanged(nameof(HasTopics));
            return;
        }

        var lines = TopicsInputBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();

        foreach (var line in lines)
        {
            var isYoutube = YoutubeRegex.IsMatch(line);
            var displayText = isYoutube
                ? line.Length > 60 ? line.Substring(0, 57) + "..." : line
                : (line.Length > 50 ? line.Substring(0, 47) + "..." : line);

            PreviewItems.Add(new PreviewItem
            {
                RawInput = line,
                DisplayText = displayText,
                IsYouTubeUrl = isYoutube
            });
        }

        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(HasTopics));
    }

    private void UpdateButtonText()
    {
        OnPropertyChanged(nameof(CreateButtonText));
        UpdatePrimaryButtonText();
    }

    public List<string> GetTopics()
    {
        if (string.IsNullOrWhiteSpace(TopicsInputBox?.Text))
            return new List<string>();

        return TopicsInputBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .ToList();
    }

    public GeminiTaskModel CreateTaskFromTemplate(string topic)
    {
        var template = SelectedTemplate ?? new GeminiTaskModel();
        return new GeminiTaskModel
        {
            Topic = topic,
            SelectedScriptwriterGem = template.SelectedScriptwriterGem,
            ScriptwriterModel = template.ScriptwriterModel,
            EnableDeepResearch = template.EnableDeepResearch,
            TargetLanguage = template.TargetLanguage,
            SelectedSceneCreatorGem = template.SelectedSceneCreatorGem,
            SceneCreatorModel = template.SceneCreatorModel,
            SelectedImageProvider = template.SelectedImageProvider,
            CharacterRef = template.CharacterRef,
            VoiceId = template.VoiceId,
            ScriptMinWords = template.ScriptMinWords,
            ScriptTargetWords = template.ScriptTargetWords,
            ScriptMaxWords = template.ScriptMaxWords,
            Status = NodeStatus.Idle,
            Step1Status = NodeStatus.Idle,
            Step2Status = NodeStatus.Idle,
            Step3Status = NodeStatus.Idle,
            Step4Status = NodeStatus.Idle,
        };
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Validation: need at least 1 topic
        if (!HasTopics)
        {
            args.Cancel = true;
            TopicsInputBox.Focus(FocusState.Programmatic);
        }
    }

    private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Cancel - nothing to do
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
