using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AssetAutomator.WinUI.Views.Dialogs;

public sealed partial class TaskProfileDialog : ContentDialog
{
    private readonly TaskProfile _profile;
    private readonly bool _isNew;
    private readonly ObservableCollection<GemOptionItem> _availableScriptwriterGems;
    private readonly ObservableCollection<GemOptionItem> _availableSceneCreatorGems;
    private readonly ObservableCollection<string> _availableAiModels;
    private readonly ObservableCollection<string> _availableImageProviders;

    public TaskProfile ResultProfile { get; private set; } = null!;
    public bool DialogResult { get; private set; }

    /// <summary>
    /// Opens the dialog for creating a new profile or editing an existing one.
    /// </summary>
    /// <param name="profileToEdit">Pass an existing profile to edit; pass null to create new.</param>
    public TaskProfileDialog(
        TaskProfile? profileToEdit,
        ObservableCollection<GemOptionItem> availableScriptwriterGems,
        ObservableCollection<GemOptionItem> availableSceneCreatorGems,
        ObservableCollection<string> availableAiModels,
        ObservableCollection<string> availableImageProviders)
    {
        InitializeComponent();

        _profile = profileToEdit ?? new TaskProfile();
        _isNew = profileToEdit == null;
        _availableScriptwriterGems = availableScriptwriterGems;
        _availableSceneCreatorGems = availableSceneCreatorGems;
        _availableAiModels = availableAiModels;
        _availableImageProviders = availableImageProviders;

        Title = _isNew ? "Tạo Profile Mới" : $"Chỉnh Sửa Profile: {_profile.Name}";

        PopulateForm();
    }

    private void PopulateForm()
    {
        TxtProfileName.Text = _profile.Name;

        // Scriptwriter
        CmbScriptwriterGem.ItemsSource = _availableScriptwriterGems;
        CmbScriptwriterModel.ItemsSource = _availableAiModels;

        if (_availableScriptwriterGems.Count > 0)
        {
            var swGem = _availableScriptwriterGems.FirstOrDefault(g =>
                string.Equals(g.Id, _profile.ScriptwriterGemId, StringComparison.OrdinalIgnoreCase))
                ?? (_availableScriptwriterGems.FirstOrDefault());

            if (swGem != null)
            {
                CmbScriptwriterGem.SelectedItem = swGem;
                _profile.ScriptwriterGemId = swGem.Id;
                _profile.ScriptwriterGemName = swGem.Name;
            }
        }

        CmbScriptwriterModel.SelectedItem = _availableAiModels
            .FirstOrDefault(m => string.Equals(m, _profile.ScriptwriterModel, StringComparison.OrdinalIgnoreCase))
            ?? _availableAiModels.FirstOrDefault();

        ChkDeepResearch.IsChecked = true; // Always true; UI is disabled.
        _profile.EnableDeepResearch = true;

        // Scene Creator
        CmbSceneCreatorGem.ItemsSource = _availableSceneCreatorGems;
        CmbSceneCreatorModel.ItemsSource = _availableAiModels;

        if (_availableSceneCreatorGems.Count > 0)
        {
            var scGem = _availableSceneCreatorGems.FirstOrDefault(g =>
                string.Equals(g.Id, _profile.SceneCreatorGemId, StringComparison.OrdinalIgnoreCase))
                ?? (_availableSceneCreatorGems.FirstOrDefault());

            if (scGem != null)
            {
                CmbSceneCreatorGem.SelectedItem = scGem;
                _profile.SceneCreatorGemId = scGem.Id;
                _profile.SceneCreatorGemName = scGem.Name;
            }
        }

        CmbSceneCreatorModel.SelectedItem = _availableAiModels
            .FirstOrDefault(m => string.Equals(m, _profile.SceneCreatorModel, StringComparison.OrdinalIgnoreCase))
            ?? _availableAiModels.FirstOrDefault();

        // API Stream mode is hardcoded; no toggle UI anymore.
        _profile.UseApiStreamForSceneCreator = true;

        // Voice
        TxtVoiceId.Text = _profile.VoiceId;

        // Image
        CmbImageProvider.ItemsSource = _availableImageProviders;
        CmbImageProvider.SelectedItem = _availableImageProviders
            .FirstOrDefault(p => string.Equals(p, _profile.SelectedImageProvider, StringComparison.OrdinalIgnoreCase))
            ?? _availableImageProviders.FirstOrDefault();

        TxtCharacterRef.Text = _profile.CharacterRef;
    }

    private void TxtProfileName_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtNameError.Visibility = string.IsNullOrWhiteSpace(TxtProfileName.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(TxtProfileName.Text))
        {
            TxtNameError.Visibility = Visibility.Visible;
            args.Cancel = true;
            return;
        }

        // Gather values from UI
        _profile.Name = TxtProfileName.Text.Trim();

        if (CmbScriptwriterGem.SelectedItem is GemOptionItem swGem)
        {
            _profile.ScriptwriterGemId = swGem.Id;
            _profile.ScriptwriterGemName = swGem.Name;
        }

        _profile.ScriptwriterModel = CmbScriptwriterModel.SelectedItem as string ?? "gemini-3-flash-plus";
        _profile.EnableDeepResearch = true; // Hardcoded — Deep Research is always on.

        if (CmbSceneCreatorGem.SelectedItem is GemOptionItem scGem)
        {
            _profile.SceneCreatorGemId = scGem.Id;
            _profile.SceneCreatorGemName = scGem.Name;
        }

        _profile.SceneCreatorModel = CmbSceneCreatorModel.SelectedItem as string ?? "gemini-3-flash-plus";
        _profile.UseApiStreamForSceneCreator = true; // Hardcoded — always API Stream.

        _profile.VoiceId = TxtVoiceId.Text?.Trim() ?? string.Empty;
        _profile.SelectedImageProvider = CmbImageProvider.SelectedItem as string ?? "flow_local";
        _profile.CharacterRef = TxtCharacterRef.Text?.Trim() ?? string.Empty;

        ResultProfile = _profile;
        DialogResult = true;
    }

    private async void BtnBrowseVoice_Click(object sender, RoutedEventArgs e)
    {
        var configService = App.Services.GetService<IConfigService>();
        string apiKey = configService?.CurrentSettings.Ai84ApiKey ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
        {
            var warnDialog = new ContentDialog
            {
                Title = "Cần AI84 API Key",
                Content = "Vui lòng nhập AI84 API Key trong phần Cài đặt (Settings) trước khi tìm & chọn giọng đọc.",
                CloseButtonText = "Đóng",
                XamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? XamlRoot
            };
            await warnDialog.ShowAsync();
            return;
        }

        // WinUI 3 limitation: showing a ContentDialog from inside another ContentDialog's
        // button click handler hangs the UI because both dialogs try to mount their popup
        // overlay on the same XamlRoot. The supported workaround is to HIDE the parent
        // dialog, show the inner dialog, then re-show the parent with the selected value.
        var rootXaml = App.MainWindowInstance?.Content?.XamlRoot ?? XamlRoot;
        if (rootXaml == null) return;

        // Remember the current voice ID so we can apply the selection on return.
        string previousVoiceId = TxtVoiceId.Text;

        // Hide THIS dialog so the inner one can mount its popup overlay. Wait a frame so
        // the popup overlay teardown is fully flushed before we open the child — opening
        // a new ContentDialog on the same render tick as the parent's hide would still
        // race with the popup cleanup.
        this.Hide();
        await Task.Yield();
        // Small extra delay to be safe — popup teardown is async on WinUI 3.
        await Task.Delay(50);

        var dialog = new VoiceSelectorDialog(apiKey, previousVoiceId) { XamlRoot = rootXaml };
        var res = await dialog.ShowAsync();

        if (res == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.SelectedVoiceId))
        {
            previousVoiceId = dialog.SelectedVoiceId;
        }

        // Re-show this dialog with the (possibly updated) voice id.
        TxtVoiceId.Text = previousVoiceId;
        await this.ShowAsync();
    }

    private async void BtnBrowseCharRef_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".webp");

        if (App.MainWindowInstance != null)
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            TxtCharacterRef.Text = file.Path;
        }
    }
}
