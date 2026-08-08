using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Linq;
using System.Threading.Tasks;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;
using AssetAutomator.WinUI.Views.Dialogs;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class GeminiPage : Page
{
    public GeminiViewModel ViewModel { get; }

    public GeminiPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<GeminiViewModel>();
        DataContext = ViewModel;

        // Auto-scroll console logs to bottom whenever content changes
        ConsoleLogsTextBox.TextChanged += ConsoleLogsTextBox_TextChanged;

        Loaded += GeminiPage_Loaded;
        Unloaded += GeminiPage_Unloaded;
    }

    private void GeminiPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Column widths are now bound declaratively in XAML to
        // ViewModel.EffectiveDetailColumnWidth, so no imperative sync is needed.
    }

    private void GeminiPage_Unloaded(object sender, RoutedEventArgs e)
    {
    }

    private void LstGeminiTasks_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Whenever the selection changes (programmatic or click), keep the detail panel
        // open for the newly selected task. The previous UX closed the panel when the
        // user clicked a different row, which made the master-detail navigation feel broken.
        if (ViewModel.SelectedTask != null)
        {
            OpenDetailPanelFor(ViewModel.SelectedTask);
        }
    }

    /// <summary>
    /// Fired by the ListView when the user clicks *any* row — including the row that is
    /// already selected. This is the only event that reliably fires on a same-row click,
    /// because WinUI suppresses <see cref="LstGeminiTasks_SelectionChanged"/> when
    /// SelectedItem has not actually changed. We keep this handler minimal: it only
    /// (re-)opens the detail panel and rebuilds its content. The underlying SelectedTask
    /// is left alone so we don't fight the binding when the click came from the already-
    /// selected row.
    /// </summary>
    private void LstGeminiTasks_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GeminiTaskModel task) return;
        OpenDetailPanelFor(task);
    }

    private void OpenDetailPanelFor(GeminiTaskModel task)
    {
        ViewModel.SelectedTask = task;
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.Configuration;
        TaskDetailsContentHost.Content = BuildRowDetailsContent(task);
        ViewModel.IsDetailPanelVisible = true;
    }

    private void ConsoleLogsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ConsoleAutoScrollCheck?.IsChecked != true) return;
        // Defer to next render frame so the new content is measured first.
        DispatcherQueue.TryEnqueue(() =>
        {
            ConsoleLogsScrollViewer?.ChangeView(null, double.MaxValue, null, disableAnimation: true);
        });
    }

    private void CopyConsoleLogsButton_Click(object sender, RoutedEventArgs e)
    {
        string text = ViewModel?.ConsoleLogs ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            ShowTransientStatus("Console log trống — không có gì để copy.");
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(text);
        Clipboard.SetContent(dataPackage);

        // Provide user-visible feedback (toast-style status in the page header).
        int lines = text.Count(c => c == '\n');
        ShowTransientStatus($"📋 Đã copy {text.Length:N0} ký tự ({lines:N0} dòng) vào clipboard.");
    }

    private void ShowTransientStatus(string message)
    {
        // Push the message into the same StatusLog the rest of the VM uses so
        // users see confirmation in the page footer without an extra dialog.
        if (ViewModel is null) return;
        var prop = typeof(GeminiViewModel).GetProperty("StatusLog");
        prop?.SetValue(ViewModel, message);
    }

    private void BtnShowRowDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GeminiTaskModel task) return;

        // If clicking the same task while panel is open on Config tab, toggle close it
        if (ViewModel.IsDetailPanelVisible
            && ViewModel.SelectedTask?.Id == task.Id
            && ViewModel.ActiveDetailTab == GeminiViewModel.DetailTab.Configuration)
        {
            ViewModel.CloseDetailPanelCommand.Execute(null);
            return;
        }

        ViewModel.SelectedTask = task;
        TaskDetailsContentHost.Content = BuildRowDetailsContent(task);
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.Configuration;
        ViewModel.IsDetailPanelVisible = true;
    }

    private void BtnShowTaskLogs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GeminiTaskModel task) return;

        // If clicking the same task while panel is open on Logs tab, toggle close it
        if (ViewModel.IsDetailPanelVisible
            && ViewModel.SelectedTask?.Id == task.Id
            && ViewModel.ActiveDetailTab == GeminiViewModel.DetailTab.LiveLogs)
        {
            ViewModel.CloseDetailPanelCommand.Execute(null);
            return;
        }

        ViewModel.SelectedTask = task;
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.LiveLogs;
        ViewModel.IsDetailPanelVisible = true;
    }

    private void TabConfig_Click(object sender, RoutedEventArgs e)
    {
        // Ensure config form is populated for the currently selected task.
        if (ViewModel.SelectedTask != null && TaskDetailsContentHost.Content == null)
        {
            TaskDetailsContentHost.Content = BuildRowDetailsContent(ViewModel.SelectedTask);
        }
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.Configuration;
    }

    private void TabLogs_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.LiveLogs;
    }

    private async void BtnNewProfile_Click(object sender, RoutedEventArgs e)
    {
        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot;
        if (xamlRoot == null) return;

        var dialog = new AssetAutomator.WinUI.Views.Dialogs.TaskProfileDialog(
            profileToEdit: null,
            ViewModel.AvailableScriptwriterGems,
            ViewModel.AvailableSceneCreatorGems,
            ViewModel.AvailableAiModels,
            ViewModel.AvailableImageProviders)
        {
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.ResultProfile != null)
        {
            var manager = new AssetAutomator.Infrastructure.Services.TaskProfileManager();
            manager.AddProfile(dialog.ResultProfile);

            // Refresh the profile list in the VM
            ViewModel.TaskProfiles.Clear();
            foreach (var p in manager.Profiles)
            {
                ViewModel.TaskProfiles.Add(p);
            }

            ViewModel.StatusLog = $"✅ Đã tạo profile mới: '{dialog.ResultProfile.Name}'";
        }
    }

    private void BtnApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TaskProfiles.Count == 0)
        {
            // No profiles yet — offer to create one
            BtnNewProfile_Click(sender, e);
            return;
        }

        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot;
        if (xamlRoot == null) return;

        var flyout = new MenuFlyout();

        var createItem = new MenuFlyoutItem
        {
            Text = "+ Tạo profile mới...",
            Icon = new FontIcon { Glyph = "\uE710" }
        };
        createItem.Click += (s, args) => BtnNewProfile_Click(sender, e);
        flyout.Items.Add(createItem);

        if (ViewModel.TaskProfiles.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            foreach (var profile in ViewModel.TaskProfiles)
            {
                var item = new MenuFlyoutItem
                {
                    Text = profile.Name,
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                var captured = profile;
                item.Click += (s, args) =>
                {
                    ViewModel.ApplyProfileToSelectedTasksCommand.Execute(captured);
                    // Refresh detail panel content
                    if (ViewModel.SelectedTask != null)
                    {
                        TaskDetailsContentHost.Content = BuildRowDetailsContent(ViewModel.SelectedTask);
                    }
                };
                flyout.Items.Add(item);
            }
        }

        var btn = sender as Button;
        flyout.ShowAt(btn ?? BtnApplyProfile);
    }

    private void BtnCloseTaskDetailsDrawer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseDetailPanelCommand.Execute(null);
    }

    private async void BtnManageProfiles_Click(object sender, RoutedEventArgs e)
    {
        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot;
        if (xamlRoot == null) return;

        var manager = new AssetAutomator.Infrastructure.Services.TaskProfileManager();

        var flyout = new MenuFlyout();

        var createItem = new MenuFlyoutItem { Text = "+ Tạo profile mới...", Icon = new FontIcon { Glyph = "\uE710" } };
        createItem.Click += async (s, args) =>
        {
            var dialog = new AssetAutomator.WinUI.Views.Dialogs.TaskProfileDialog(
                null,
                ViewModel.AvailableScriptwriterGems,
                ViewModel.AvailableSceneCreatorGems,
                ViewModel.AvailableAiModels,
                ViewModel.AvailableImageProviders) { XamlRoot = xamlRoot };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && dialog.ResultProfile != null)
            {
                manager.AddProfile(dialog.ResultProfile);
                RefreshProfilesInViewModel();
                ViewModel.StatusLog = $"✅ Đã tạo profile: '{dialog.ResultProfile.Name}'";
            }
        };
        flyout.Items.Add(createItem);

        if (ViewModel.TaskProfiles.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());

            foreach (var profile in ViewModel.TaskProfiles)
            {
                var applyAll = new MenuFlyoutItem
                {
                    Text = $"🔗 Áp dụng '{profile.Name}' cho tất cả",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                var captured = profile;
                applyAll.Click += (s, args) =>
                {
                    ViewModel.ApplyProfileToAllTasksCommand.Execute(captured);
                    ViewModel.StatusLog = $"✅ Đã áp dụng '{captured.Name}' cho {ViewModel.GeminiTasks.Count} task(s).";
                };

                var editItem = new MenuFlyoutItem
                {
                    Text = $"✏️ Chỉnh sửa '{profile.Name}'",
                    Icon = new FontIcon { Glyph = "\uE70F" }
                };
                editItem.Click += async (s, args) =>
                {
                    var dialog = new AssetAutomator.WinUI.Views.Dialogs.TaskProfileDialog(
                        captured,
                        ViewModel.AvailableScriptwriterGems,
                        ViewModel.AvailableSceneCreatorGems,
                        ViewModel.AvailableAiModels,
                        ViewModel.AvailableImageProviders) { XamlRoot = xamlRoot };

                    var result = await dialog.ShowAsync();
                    if (result == ContentDialogResult.Primary && dialog.ResultProfile != null)
                    {
                        manager.UpdateProfile(dialog.ResultProfile);
                        RefreshProfilesInViewModel();
                        ViewModel.StatusLog = $"✅ Đã cập nhật profile: '{dialog.ResultProfile.Name}'";
                    }
                };

                var deleteItem = new MenuFlyoutItem
                {
                    Text = $"🗑️ Xóa '{profile.Name}'",
                    Icon = new FontIcon { Glyph = "\uE74D" }
                };
                deleteItem.Click += (s, args) =>
                {
                    manager.RemoveProfile(captured.Id);
                    RefreshProfilesInViewModel();
                    ViewModel.StatusLog = $"🗑️ Đã xóa profile: '{captured.Name}'";
                };

                var sub = new MenuFlyoutSubItem { Text = profile.Name };
                sub.Items.Add(applyAll);
                sub.Items.Add(editItem);
                sub.Items.Add(new MenuFlyoutSeparator());
                sub.Items.Add(deleteItem);
                flyout.Items.Add(sub);
            }
        }

        if (sender is Button clickedBtn)
            flyout.ShowAt(clickedBtn);
        else
            flyout.ShowAt(BtnManageProfiles);
    }

    private void RefreshProfilesInViewModel()
    {
        var manager = new AssetAutomator.Infrastructure.Services.TaskProfileManager();
        ViewModel.TaskProfiles.Clear();
        foreach (var p in manager.Profiles)
        {
            ViewModel.TaskProfiles.Add(p);
        }
    }

    private async void BtnRunSingleTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GeminiTaskModel task) return;

        if (ViewModel.IsGenerating)
        {
            await ShowInfoAsync("Đang có pipeline chạy", "Một pipeline khác đang chạy. Vui lòng hủy hoặc đợi phiên hiện tại hoàn thành trước khi chạy task mới.");
            return;
        }

        // Mirror WPF: open the live-logs tab in the detail panel for the chosen task so the user
        // sees per-step progress immediately while the pipeline is running.
        ViewModel.SelectedTask = task;
        ViewModel.ActiveDetailTab = GeminiViewModel.DetailTab.LiveLogs;
        ViewModel.IsDetailPanelVisible = true;
        ViewModel.IsConsoleLogVisible = true;

        await ViewModel.RunSingleTaskCommand.ExecuteAsync(task);
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var xamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot;
        if (xamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Đóng",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };
        await dialog.ShowAsync();
    }

    private bool _isDraggingDetailPanel;
    private double _detailDragStartX;
    private double _detailDragStartWidth;

    private void DetailPanelResizeHandle_ManipulationStarted(object sender, Microsoft.UI.Xaml.Input.ManipulationStartedRoutedEventArgs e)
    {
        _isDraggingDetailPanel = true;
        _detailDragStartX = e.Position.X;
        _detailDragStartWidth = ViewModel.DetailPanelWidth;
    }

    private void DetailPanelResizeHandle_ManipulationDelta(object sender, Microsoft.UI.Xaml.Input.ManipulationDeltaRoutedEventArgs e)
    {
        if (!_isDraggingDetailPanel) return;

        // Drag handle is on the LEFT edge of the detail panel.
        // The panel's right edge is anchored to the page; only the left edge moves.
        // So pulling the handle RIGHT moves the left edge right → panel gets NARROWER.
        //    pulling the handle LEFT  moves the left edge left  → panel gets WIDER.
        double delta = e.Cumulative.Translation.X;
        double newWidth = _detailDragStartWidth - delta;

        double minW = 380;
        double maxW = Math.Max(900, PageRoot.ActualWidth * 0.75);
        newWidth = Math.Max(minW, Math.Min(newWidth, maxW));

        ViewModel.DetailPanelWidth = newWidth;
    }

    private void DetailPanelResizeHandle_ManipulationCompleted(object sender, Microsoft.UI.Xaml.Input.ManipulationCompletedRoutedEventArgs e)
    {
        _isDraggingDetailPanel = false;
    }

    private Flyout BuildRowDetailsFlyout(GeminiTaskModel task)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Right,
            FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
            {
                Setters =
                {
                    new Setter(FlyoutPresenter.MinWidthProperty, 720),
                    new Setter(FlyoutPresenter.MaxWidthProperty, 900),
                    new Setter(FlyoutPresenter.PaddingProperty, new Thickness(16))
                }
            }
        };

        flyout.Content = BuildRowDetailsContent(task);
        return flyout;
    }

    private FrameworkElement BuildRowDetailsContent(GeminiTaskModel task)
    {
        var mainGrid = new Grid
        {
            RowSpacing = 12,
            ColumnSpacing = 12
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Row 0: Scriptwriter (left) | Scene Creator (right) ──
        AddScriptwriterPanel(mainGrid, task, row: 0, col: 0);
        AddSceneCreatorPanel(mainGrid, task, row: 0, col: 1);

        // ── Row 1: Voice | Provider | Character Ref (full width) ──
        var voiceRowCard = AddVoiceProviderCharacterRow(mainGrid, task);
        Grid.SetRow(voiceRowCard, 1);
        Grid.SetColumnSpan(voiceRowCard, 2);

        // ── Row 2: Channel URL / Suggested topics helper ──
        var topicCard = AddTopicSuggestionPanel(mainGrid, task);
        Grid.SetRow(topicCard, 2);
        Grid.SetColumnSpan(topicCard, 2);

        // ── Row 3: Actions row (full width) ──
        var actionsCard = AddActionsPanel(mainGrid, task);
        Grid.SetRow(actionsCard, 3);
        Grid.SetColumnSpan(actionsCard, 2);

        return mainGrid;
    }

    private Border AddScriptwriterPanel(Grid parent, GeminiTaskModel task, int row, int col)
    {
        var card = new Border
        {
            Background = Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var stack = new StackPanel { Spacing = 8 };

        var header = new TextBlock
        {
            Text = "🎬 Scriptwriter",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock { Text = "Gem", FontSize = 11, Opacity = 0.7 });
        var scriptwriterGemCombo = new ComboBox
        {
            ItemsSource = ViewModel.AvailableScriptwriterGems,
            SelectedItem = task.SelectedScriptwriterGem,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Chọn gem scriptwriter"
        };
        scriptwriterGemCombo.SelectionChanged += (s, e) =>
        {
            if (scriptwriterGemCombo.SelectedItem is GemOptionItem gem)
            {
                task.SelectedScriptwriterGem = gem;
            }
        };
        stack.Children.Add(scriptwriterGemCombo);

        stack.Children.Add(new TextBlock { Text = "Model", FontSize = 11, Opacity = 0.7 });
        var scriptwriterModelCombo = new ComboBox
        {
            ItemsSource = ViewModel.AvailableAiModels,
            SelectedItem = task.ScriptwriterModel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        scriptwriterModelCombo.SelectionChanged += (s, e) =>
        {
            if (scriptwriterModelCombo.SelectedItem is string m)
            {
                task.ScriptwriterModel = m;
            }
        };
        stack.Children.Add(scriptwriterModelCombo);

        var deepResearchCheck = new CheckBox
        {
            Content = "Bật Deep Research",
            IsChecked = task.EnableDeepResearch,
            IsHitTestVisible = false,
            IsEnabled = false,
            Opacity = 0.8
        };
        // Deep Research is hardcoded to true — the toggle is disabled so the user
        // can see the value but cannot change it.
        deepResearchCheck.Checked += (s, e) => task.EnableDeepResearch = true;
        deepResearchCheck.Unchecked += (s, e) => task.EnableDeepResearch = true;
        // Force the value back to true even if the binding tries to set it false.
        task.EnableDeepResearch = true;
        stack.Children.Add(deepResearchCheck);

        card.Child = stack;
        Grid.SetRow(card, row);
        Grid.SetColumn(card, col);
        parent.Children.Add(card);
        return card;
    }

    private Border AddSceneCreatorPanel(Grid parent, GeminiTaskModel task, int row, int col)
    {
        var card = new Border
        {
            Background = Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var stack = new StackPanel { Spacing = 8 };

        stack.Children.Add(new TextBlock
        {
            Text = "🎭 Scene Creator",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        });

        stack.Children.Add(new TextBlock { Text = "Gem", FontSize = 11, Opacity = 0.7 });
        var sceneGemCombo = new ComboBox
        {
            ItemsSource = ViewModel.AvailableSceneCreatorGems,
            SelectedItem = task.SelectedSceneCreatorGem,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Chọn gem scene creator"
        };
        sceneGemCombo.SelectionChanged += (s, e) =>
        {
            if (sceneGemCombo.SelectedItem is GemOptionItem gem)
            {
                task.SelectedSceneCreatorGem = gem;
            }
        };
        stack.Children.Add(sceneGemCombo);

        stack.Children.Add(new TextBlock { Text = "Model", FontSize = 11, Opacity = 0.7 });
        var sceneModelCombo = new ComboBox
        {
            ItemsSource = ViewModel.AvailableAiModels,
            SelectedItem = task.SceneCreatorModel,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        sceneModelCombo.SelectionChanged += (s, e) =>
        {
            if (sceneModelCombo.SelectedItem is string m)
            {
                task.SceneCreatorModel = m;
            }
        };
        stack.Children.Add(sceneModelCombo);

        // ── Mode: always API Stream (hardcoded) ──
        // API Stream mode calls the Python REST /api/chat/stream-extended endpoint,
        // mirroring test_gem_and_thinking.py — uploads SRT + transcript, streams
        // extended thinking + scenes JSON back in realtime, no Chrome required.
        // Playwright mode was removed; the task always uses API Stream for speed and
        // reliability. The value is forced to true so any legacy data still works.
        task.UseApiStreamForSceneCreator = true;
        var modeInfoBadge = new TextBlock
        {
            Text = "📡 API Stream (luôn bật — không cần Chrome)",
            FontSize = 11,
            Opacity = 0.8,
            Margin = new Thickness(0, 4, 0, 0)
        };
        stack.Children.Add(modeInfoBadge);

        card.Child = stack;
        Grid.SetRow(card, row);
        Grid.SetColumn(card, col);
        parent.Children.Add(card);
        return card;
    }

    private Border AddVoiceProviderCharacterRow(Grid parent, GeminiTaskModel task)
    {
        var card = new Border
        {
            Background = Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Voice ID
        var voiceStack = new StackPanel { Spacing = 4 };
        voiceStack.Children.Add(new TextBlock { Text = "🎙️ Voice ID", FontSize = 11, Opacity = 0.7 });
        var voiceRow = new Grid();
        voiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        voiceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var voiceBox = new TextBox { Text = task.VoiceId, PlaceholderText = "vd: en-US-Standard-A" };
        voiceBox.TextChanged += (s, e) => task.VoiceId = voiceBox.Text;
        Grid.SetColumn(voiceBox, 0);

        var browseBtn = new Button { Content = "🔍" };
        ToolTipService.SetToolTip(browseBtn, "Tìm & Chọn Voice ID");
        browseBtn.Click += async (s, e) =>
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
                    XamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot
                };
                await warnDialog.ShowAsync();
                return;
            }

            var dialog = new VoiceSelectorDialog(apiKey, task.VoiceId)
            {
                XamlRoot = App.MainWindowInstance?.Content?.XamlRoot ?? PageRoot.XamlRoot
            };
            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.SelectedVoiceId))
            {
                task.VoiceId = dialog.SelectedVoiceId;
                voiceBox.Text = dialog.SelectedVoiceId;
            }
        };
        Grid.SetColumn(browseBtn, 1);
        voiceRow.Children.Add(voiceBox);
        voiceRow.Children.Add(browseBtn);
        voiceStack.Children.Add(voiceRow);
        Grid.SetColumn(voiceStack, 0);
        grid.Children.Add(voiceStack);

        // Provider
        var provStack = new StackPanel { Spacing = 4 };
        provStack.Children.Add(new TextBlock { Text = "🖼️ Provider", FontSize = 11, Opacity = 0.7 });
        var provCombo = new ComboBox
        {
            ItemsSource = ViewModel.AvailableImageProviders,
            SelectedItem = task.SelectedImageProvider,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        provCombo.SelectionChanged += (s, e) =>
        {
            if (provCombo.SelectedItem is string p)
            {
                task.SelectedImageProvider = p;
            }
        };
        provStack.Children.Add(provCombo);
        Grid.SetColumn(provStack, 1);
        grid.Children.Add(provStack);

        // Character Ref
        var charStack = new StackPanel { Spacing = 4 };
        charStack.Children.Add(new TextBlock { Text = "👤 Character Ref", FontSize = 11, Opacity = 0.7 });
        var charRow = new Grid();
        charRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        charRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var charBox = new TextBox { Text = task.CharacterRef, PlaceholderText = "Đường dẫn ảnh nhân vật" };
        charBox.TextChanged += (s, e) => task.CharacterRef = charBox.Text;
        Grid.SetColumn(charBox, 0);

        var charBtn = new Button { Content = "📁" };
        ToolTipService.SetToolTip(charBtn, "Chọn file ảnh nhân vật mẫu");
        charBtn.Click += async (s, e) =>
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
                task.CharacterRef = file.Path;
                charBox.Text = file.Path;
            }
        };
        Grid.SetColumn(charBtn, 1);
        charRow.Children.Add(charBox);
        charRow.Children.Add(charBtn);
        charStack.Children.Add(charRow);
        Grid.SetColumn(charStack, 2);
        grid.Children.Add(charStack);

        card.Child = grid;
        parent.Children.Add(card);
        return card;
    }

    private Border AddTopicSuggestionPanel(Grid parent, GeminiTaskModel task)
    {
        var card = new Border
        {
            Background = Microsoft.UI.Xaml.Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var stack = new StackPanel { Spacing = 8 };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleBlock = new TextBlock
        {
            Text = "💡 Gợi Ý Chủ Đề (YouTube channel)",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13
        };
        Grid.SetColumn(titleBlock, 0);
        header.Children.Add(titleBlock);

        var suggestBtn = new Button
        {
            Command = ViewModel.SuggestTopicsCommand,
            Content = "🔍 Phân Tích Channel",
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(suggestBtn, 1);
        header.Children.Add(suggestBtn);
        stack.Children.Add(header);

        stack.Children.Add(new TextBlock
        {
            Text = "Channel URL (hoặc mô tả chủ đề):",
            FontSize = 11,
            Opacity = 0.7
        });
        var urlBox = new TextBox
        {
            Text = task.ChannelUrl,
            PlaceholderText = "https://youtube.com/@handle"
        };
        urlBox.TextChanged += (s, e) => task.ChannelUrl = urlBox.Text;
        stack.Children.Add(urlBox);

        if (task.SuggestedTopics != null && task.SuggestedTopics.Count > 0)
        {
            var listPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
            listPanel.Children.Add(new TextBlock
            {
                Text = $"Gợi ý ({task.SuggestedTopics.Count}):",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 11
            });

            var scroll = new ScrollViewer { MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var innerStack = new StackPanel { Spacing = 4 };
            foreach (var topic in task.SuggestedTopics.Take(10))
            {
                var topicRow = new Grid();
                topicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 2 };
                info.Children.Add(new TextBlock
                {
                    Text = topic.Title,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrWhiteSpace(topic.Description))
                {
                    info.Children.Add(new TextBlock
                    {
                        Text = topic.Description,
                        FontSize = 10,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                Grid.SetColumn(info, 0);
                topicRow.Children.Add(info);

                var applyBtn = new Button
                {
                    Content = "↩",
                    Padding = new Thickness(6, 2, 6, 2),
                    Command = ViewModel.ApplySuggestedTopicCommand,
                    CommandParameter = topic
                };
                ToolTipService.SetToolTip(applyBtn, "Áp dụng topic này");
                Grid.SetColumn(applyBtn, 1);
                topicRow.Children.Add(applyBtn);

                innerStack.Children.Add(topicRow);
            }
            scroll.Content = innerStack;
            listPanel.Children.Add(scroll);
            stack.Children.Add(listPanel);
        }

        card.Child = stack;
        parent.Children.Add(card);
        return card;
    }

    private Border AddActionsPanel(Grid parent, GeminiTaskModel task)
    {
        var card = new Border
        {
            Background = Microsoft.UI.Xaml.Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush,
            BorderBrush = Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var runBtn = new Button
        {
            Style = Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"] as Style,
            Command = ViewModel.RunSingleTaskCommand,
            CommandParameter = task
        };
        var runStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        runStack.Children.Add(new FontIcon { Glyph = "\uE768", FontSize = 12 });
        runStack.Children.Add(new TextBlock { Text = "Chạy task này", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        runBtn.Content = runStack;
        Grid.SetColumn(runBtn, 0);
        grid.Children.Add(runBtn);

        // Single tabbed "View Assets" button — opens a popup with 3 tabs:
        // scenes.json · transcript.txt · voiceover.srt. Replaces the previous
        // pair of separate viewers so the user clicks once and can flip between
        // any of the three task outputs.
        var assetsBtn = new Button
        {
            Command = ViewModel.ViewTaskAssetsCommand,
            CommandParameter = task
        };
        ToolTipService.SetToolTip(assetsBtn,
            "Mở popup xem scenes.json / transcript.txt / voiceover.srt của task này");
        var assetsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        assetsStack.Children.Add(new FontIcon { Glyph = "\uE8B7", FontSize = 12 });
        assetsStack.Children.Add(new TextBlock { Text = "View Assets", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        assetsBtn.Content = assetsStack;
        Grid.SetColumn(assetsBtn, 1);
        grid.Children.Add(assetsBtn);

        var folderBtn = new Button
        {
            Command = ViewModel.OpenTaskFolderCommand,
            CommandParameter = task
        };
        var folderStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        folderStack.Children.Add(new FontIcon { Glyph = "\uE838", FontSize = 12 });
        folderStack.Children.Add(new TextBlock { Text = "Mở thư mục" });
        folderBtn.Content = folderStack;
        Grid.SetColumn(folderBtn, 2);
        grid.Children.Add(folderBtn);

        var deleteBtn = new Button
        {
            Command = ViewModel.DeleteSingleTaskCommand,
            CommandParameter = task
        };
        var deleteStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        deleteStack.Children.Add(new FontIcon { Glyph = "\uE74D", FontSize = 12, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) });
        deleteStack.Children.Add(new TextBlock { Text = "Xóa task" });
        deleteBtn.Content = deleteStack;
        Grid.SetColumn(deleteBtn, 4);
        grid.Children.Add(deleteBtn);

        card.Child = grid;
        parent.Children.Add(card);
        return card;
    }
}
