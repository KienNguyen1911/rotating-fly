using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Linq;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;
using AssetAutomator.WinUI.Views.Dialogs;
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
    }

    private void BtnShowRowDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GeminiTaskModel task) return;

        // If clicking the same task while drawer is open, toggle close it
        if (ViewModel.IsRowDetailsDrawerOpen && ViewModel.SelectedTask?.Id == task.Id)
        {
            BtnCloseTaskDetailsDrawer_Click(sender, e);
            return;
        }

        ViewModel.SelectedTask = task;
        TxtTaskDetailsDrawerTitle.Text = $"⚙️ Cấu Hình Chi Tiết Task: {task.Topic}";
        TaskDetailsContentHost.Content = BuildRowDetailsContent(task);

        if (!ViewModel.IsRowDetailsDrawerOpen)
        {
            ViewModel.IsRowDetailsDrawerOpen = true;
            AnimateDrawerSlideUp();
        }
    }

    private void BtnCloseTaskDetailsDrawer_Click(object sender, RoutedEventArgs e)
    {
        AnimateDrawerSlideDown(() =>
        {
            ViewModel.IsRowDetailsDrawerOpen = false;
        });
    }

    private void AnimateDrawerSlideUp()
    {
        TaskDetailsDrawerTransform.Y = 300;
        var animation = new DoubleAnimation
        {
            From = 300,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(animation, TaskDetailsDrawerTransform);
        Storyboard.SetTargetProperty(animation, "Y");
        sb.Children.Add(animation);
        sb.Begin();
    }

    private void AnimateDrawerSlideDown(Action completed)
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 300,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(animation, TaskDetailsDrawerTransform);
        Storyboard.SetTargetProperty(animation, "Y");
        sb.Children.Add(animation);
        sb.Completed += (s, e) => completed();
        sb.Begin();
    }

    private void BtnShowTaskLogs_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is GeminiTaskModel task)
        {
            ViewModel.OpenTaskLogsCommand.Execute(task);
        }
    }

    private bool _isDraggingDrawer;
    private double _dragStartX;
    private double _dragStartWidth;

    private void DrawerDragHandle_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var pp = e.GetCurrentPoint(TaskLogsDrawer);
        if (!pp.Properties.IsLeftButtonPressed) return;
        _isDraggingDrawer = true;
        _dragStartX = e.GetCurrentPoint(PageRoot).Position.X;
        _dragStartWidth = ViewModel.TaskLogsDrawerWidth;
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void DrawerDragHandle_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingDrawer) return;

        double currentX = e.GetCurrentPoint(PageRoot).Position.X;
        double delta = _dragStartX - currentX; // Dragging left increases width
        double newWidth = _dragStartWidth + delta;

        double maxWidth = Math.Max(400, PageRoot.ActualWidth * 0.75);
        newWidth = Math.Max(350, Math.Min(newWidth, maxWidth));

        ViewModel.TaskLogsDrawerWidth = newWidth;
    }

    private void DrawerDragHandle_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingDrawer) return;
        _isDraggingDrawer = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        e.Handled = true;
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
            IsChecked = task.EnableDeepResearch
        };
        deepResearchCheck.Checked += (s, e) => task.EnableDeepResearch = true;
        deepResearchCheck.Unchecked += (s, e) => task.EnableDeepResearch = false;
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

        var scenesBtn = new Button
        {
            Command = ViewModel.ViewTaskScenesCommand,
            CommandParameter = task
        };
        var scenesStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        scenesStack.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 12 });
        scenesStack.Children.Add(new TextBlock { Text = "Xem scenes.json" });
        scenesBtn.Content = scenesStack;
        Grid.SetColumn(scenesBtn, 1);
        grid.Children.Add(scenesBtn);

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
