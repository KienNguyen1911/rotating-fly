using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;
using AssetAutomator.Core.Models;
using AssetAutomator.WinUI.ViewModels;
using AssetAutomator.WinUI.Views.Dialogs;

namespace AssetAutomator.WinUI.Views.Pages;

public sealed partial class BatchImageGenPage : Page
{
    public BatchImageGenViewModel ViewModel { get; }

    public BatchImageGenPage()
    {
        ViewModel = App.Services.GetRequiredService<BatchImageGenViewModel>();
        InitializeComponent();

        // Display the resolved Google Flow Local launcher path so the user can
        // confirm where the auto-launcher expects main.py to live.
        try
        {
            var config = App.Services.GetService<Core.Interfaces.IConfigService>();
            if (config != null)
            {
                var path = config.CurrentSettings.GoogleFlow2RootPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    TxtGoogleFlow2RootPath.Text = $"📂 Launcher path: {path}";
                }
            }
        }
        catch
        {
            // best-effort display only
        }
    }

    private async void BtnOpenProjectsFolder_Click(object sender, RoutedEventArgs e)
    {
        // The folder is centrally managed in Settings → Projects Storage Dir, so the
        // Batch tab only exposes a quick-open shortcut. If the user wants to move
        // the folder they have to do it from Settings (single source of truth).
        string path = ViewModel.ProjectsStoragePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = App.Services.GetService<Core.Interfaces.IConfigService>()
                ?.CurrentSettings?.ProjectsStorageDir ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            var dlg = new ContentDialog
            {
                Title = "Chưa cấu hình thư mục lưu trữ dự án",
                Content = "Vui lòng vào Settings → Directories → 'Thư mục lưu Projects Batch Image Gen' để chọn thư mục.",
                CloseButtonText = "Đóng",
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            var dlg = new ContentDialog
            {
                Title = "Không thể mở thư mục",
                Content = ex.Message,
                CloseButtonText = "Đóng",
                XamlRoot = Content.XamlRoot
            };
            await dlg.ShowAsync();
        }
    }

    private async void BtnNewProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewProjectDialog
        {
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(dialog.ProjectName))
        {
            await ViewModel.OnNewProjectCreatedAsync(dialog.ProjectName);
        }
    }

    private async void BtnOpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchProjectModel project)
        {
            await ViewModel.OpenProjectAsync(project);
        }
    }

    private async void BtnDeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchProjectModel project)
        {
            var dialog = new ContentDialog
            {
                Title = "Xác nhận xóa dự án",
                Content = $"Bạn có chắc chắn muốn xóa dự án '{project.ProjectName}' không?",
                PrimaryButtonText = "Xóa",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            var res = await dialog.ShowAsync();
            if (res == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteProjectAsync(project);
            }
        }
    }

    private async void BtnBackToProjects_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.BackToProjectsAsync();
    }

    private async void BtnSaveProject_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCurrentProjectStateAsync();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string outputDir = ViewModel.OutputDir;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
        }

        Directory.CreateDirectory(outputDir);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch
        {
            // Ignore open error
        }
    }

    // Section 1: Reference Character Image Handlers
    private async void BtnSelectRefImage_Click(object sender, RoutedEventArgs e)
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

        var files = await picker.PickMultipleFilesAsync();
        if (files != null && files.Count > 0)
        {
            foreach (var file in files)
            {
                ViewModel.AddRefImage(file.Path);
            }

            if (!string.IsNullOrWhiteSpace(ViewModel.RefPreviewImagePath) && File.Exists(ViewModel.RefPreviewImagePath))
            {
                try
                {
                    ImgRefPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(ViewModel.RefPreviewImagePath));
                }
                catch
                {
                    ImgRefPreview.Source = null;
                }
            }
        }
    }

    private void BtnClearRefImage_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearRefImages();
        ImgRefPreview.Source = null;
    }

    // Section 2: Script JSON Handlers
    private async void BtnImportJson_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");

        if (App.MainWindowInstance != null)
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            try
            {
                string jsonText = await File.ReadAllTextAsync(file.Path);
                ViewModel.ScriptJson = jsonText;
                ViewModel.UpdateScriptJson(showInfoMessage: true);
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync("Lỗi Đọc File", $"Không thể đọc file JSON: {ex.Message}");
            }
        }
    }

    private void BtnUpdateScriptJson_Click(object sender, RoutedEventArgs e)
    {
        bool success = ViewModel.UpdateScriptJson(showInfoMessage: true);
        if (!success)
        {
            _ = ShowNotificationAsync("Cảnh báo", "Không tìm thấy dữ liệu scenes hợp lệ trong JSON kịch bản.");
        }
        else
        {
            _ = ShowNotificationAsync("Hoàn tất", $"Đã cập nhật kịch bản thành công! Đã tải {ViewModel.BatchImageItems.Count} cảnh.");
        }
    }

    // Section 3: Output Directory & Config Handlers
    private async void BtnBrowseOutputDir_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        if (App.MainWindowInstance != null)
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindowInstance);
            InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            ViewModel.OutputDir = folder.Path;
        }
    }

    public Microsoft.UI.Xaml.Media.ImageSource? PathToImageSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }

    private void CboxAspect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && item.Tag is string aspect)
        {
            ViewModel.SelectedAspect = aspect;
        }
    }

    private void CboxEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && item.Tag is string engine)
        {
            ViewModel.SelectedEngine = engine;
        }
    }

    private void CboxConcurrency_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int val))
        {
            ViewModel.SelectedConcurrency = val;
        }
    }

    // Engine & Provider RadioButton Event Handlers
    private void RadProvider_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            ViewModel.SelectedProvider = tag;
        }
    }

    private void RadModel_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            ViewModel.SelectedModel = tag;
        }
    }

    private void RadUpscale_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is RadioButton rb && rb.Tag is string tag)
        {
            ViewModel.SelectedUpscale = tag;
        }
    }

    // Bottom Batch Generate Command
    private async void BtnBatchGenerate_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GenerateBatchImagesAsync(
            confirmWarning: async (title, content, confirmText) =>
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    PrimaryButtonText = confirmText,
                    CloseButtonText = "Hủy",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot
                };
                var res = await dialog.ShowAsync();
                return res == ContentDialogResult.Primary;
            },
            showNotification: (title, message) =>
            {
                _ = ShowNotificationAsync(title, message);
            }
        );
    }

    public void BtnOpenFlowProjectUrl_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenFlowProjectUrl();
    }

    public void BtnBatchViewToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleView();
    }

    public async void BtnCardRegenerate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchImageItem item)
        {
            await ViewModel.RegenerateSingleItemAsync(item);
        }
    }

    /// <summary>
    /// Re-runs the watermark-removal pipeline for all <c>Done</c> items in the
    /// active project. Backs the <c>🪄 Xóa Watermark</c> button in the editor
    /// header.
    /// </summary>
    public async void BtnRemoveWatermark_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.RemoveWatermarkCommand.CanExecute(null))
        {
            try
            {
                await ViewModel.RemoveWatermarkAsync();
                int cleaned = ViewModel.BatchImageItems.Count(i => i.WatermarkRemoved);
                string msg = cleaned > 0
                    ? $"Đã xóa thành công watermark cho {cleaned}/{ViewModel.BatchImageItems.Count} ảnh."
                    : $"Đã kiểm tra {ViewModel.BatchImageItems.Count} ảnh: Tất cả đều không phát hiện watermark Gemini (hoặc đã sạch).";
                await ShowNotificationAsync("✅ Hoàn tất", msg);
            }
            catch (Exception ex)
            {
                await ShowNotificationAsync(
                    "❌ Lỗi",
                    $"Watermark removal failed: {ex.Message}");
            }
        }
    }

    public void BtnCardOpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchImageItem item && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.ImagePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public void BtnCardCopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchImageItem item && !string.IsNullOrWhiteSpace(item.Prompt))
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(item.Prompt);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            _ = ShowNotificationAsync("Đã sao chép", "Đã sao chép prompt vào Clipboard!");
        }
    }

    // Section: hover effects on the card overlay. We toggle the action button
    // StackPanel's opacity directly (instead of Visibility) so users see a
    // smooth fade in/out, matching the screenshot reqs.
    private void CardGrid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid grid) return;

        // Action buttons (top-right) appear on hover
        var actionsStackPanel = FindVisualChild<StackPanel>(grid, "CardActions");
        if (actionsStackPanel != null)
        {
            actionsStackPanel.Opacity = 1.0;
        }

        // Transcript + prompt overlay (bottom glass bar) appears on hover so the
        // default card view stays clean and image-focused.
        var promptOverlay = FindVisualChild<Border>(grid, "CardPromptOverlay");
        if (promptOverlay != null)
        {
            promptOverlay.Opacity = 1.0;
        }

        // Lift the card border slightly for additional visual feedback.
        if (grid.Parent is Border border)
        {
            border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.LightSteelBlue);
        }
    }

    private void CardGrid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Grid grid) return;

        var actionsStackPanel = FindVisualChild<StackPanel>(grid, "CardActions");
        if (actionsStackPanel != null)
        {
            actionsStackPanel.Opacity = 0.0;
        }

        var promptOverlay = FindVisualChild<Border>(grid, "CardPromptOverlay");
        if (promptOverlay != null)
        {
            promptOverlay.Opacity = 0.0;
        }

        if (grid.Parent is Border border)
        {
            border.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["CardStrokeColorDefaultBrush"];
        }
    }

    /// <summary>
    /// Walks the visual tree of <paramref name="root"/> looking for a direct
    /// descendant of type <typeparamref name="T"/> whose x:Name matches
    /// <paramref name="name"/>. Used to reach into the templated card grid to
    /// toggle the action button overlay without binding to a unique
    /// dependency property.
    /// </summary>
    private static T? FindVisualChild<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T match && match.Name == name)
        {
            return match;
        }

        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            var result = FindVisualChild<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    public void CardImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is BatchImageItem item && !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.ImagePath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async Task ShowNotificationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Đóng",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
