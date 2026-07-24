using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AssetAutomator.Models;
using AssetAutomator.Services;
using AssetAutomator.Windows;

namespace AssetAutomator
{
    /// <summary>
    /// Partial class for MainWindow handling the Batch Image Generation tab UI and events.
    /// Supports Scene Cards Grid layout, G-Labs Webhook API (:8765) and Flow Image Local API (:8787).
    /// </summary>
    public partial class MainWindow
    {
        private readonly BatchImageGenService _batchImageGenService = new BatchImageGenService();
        private readonly BatchProjectService _batchProjectService = new BatchProjectService();
        private BatchProjectModel? _activeProject;

        public ObservableCollection<BatchImageItem> BatchImageItems { get; set; } = new ObservableCollection<BatchImageItem>();
        private readonly List<(string base64Data, string tag, string filePath)> _batchRefImages = new List<(string, string, string)>();

        private void InitializeBatchImageGenTab()
        {
            if (DgridBatchImageItems != null)
            {
                DgridBatchImageItems.ItemsSource = BatchImageItems;
            }
            if (ItemsControlBatchCards != null)
            {
                ItemsControlBatchCards.ItemsSource = BatchImageItems;
            }

            if (TxtBatchScriptJson != null && string.IsNullOrWhiteSpace(TxtBatchScriptJson.Text))
            {
                TxtBatchScriptJson.Text = @"{
  ""video_title"": ""Bedtime Psychology"",
  ""scenes"": [
    {
      ""scene"": 1,
      ""id"": ""scene_001"",
      ""transcript"": ""Hello. If you are lying in the dark right now, staring at the ceiling..."",
      ""image_prompt"": ""A cinematic medium shot of @character lying on back in a simple bed, staring intently at an invisible spot on the ceiling in a dark lo-fi bedroom. A soft golden amber neon glow...""
    },
    {
      ""scene"": 2,
      ""id"": ""scene_002"",
      ""transcript"": ""Sometimes the absolute quietest hours of the night can be the loudest for our minds."",
      ""image_prompt"": ""A stylized symbolic close-up shot focusing on the head area in a pitch black room. Swirling, chaotic, noisy golden abstract script and jagged doodle patterns radiate from...""
    },
    {
      ""scene"": 3,
      ""id"": ""scene_003"",
      ""transcript"": ""Sleep is not just rest; it is a complex psychological reset."",
      ""image_prompt"": ""A high-angle dreamlike shot of a figure sleeping. Ethereal, translucent blue and purple energy waves ripple outward from the body...""
    },
    {
      ""scene"": 4,
      ""id"": ""scene_004"",
      ""transcript"": ""But for some, the transition is where the struggle lives."",
      ""image_prompt"": ""A silhouette of a person sitting on the edge of a bed, head in hands. Shadows are long and sharp...""
    }
  ]
}";
            }

            if (TxtBatchOutputDir != null && string.IsNullOrWhiteSpace(TxtBatchOutputDir.Text))
            {
                TxtBatchOutputDir.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
            }

            // Auto parse initial sample JSON script on startup
            BtnUpdateScriptJson_Click(this, new RoutedEventArgs());
            UpdateBatchConcurrencyUI();

            // Load saved projects list
            _ = LoadProjectsListAsync();
        }

        private void RadProvider_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelModelsGlabs == null || PanelModelsFlowLocal == null) return;

            if (RadProviderFlowLocal?.IsChecked == true)
            {
                PanelModelsGlabs.Visibility = Visibility.Collapsed;
                PanelModelsFlowLocal.Visibility = Visibility.Visible;
            }
            else
            {
                PanelModelsGlabs.Visibility = Visibility.Visible;
                PanelModelsFlowLocal.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnBatchImportJson_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Chọn file Kịch bản JSON (scenes.json)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string jsonText = File.ReadAllText(openFileDialog.FileName);
                    if (TxtBatchScriptJson != null)
                    {
                        TxtBatchScriptJson.Text = jsonText;
                    }
                    BtnUpdateScriptJson_Click(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi đọc file JSON: {ex.Message}", "Lỗi Đọc File", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnUpdateScriptJson_Click(object sender, RoutedEventArgs e)
        {
            string jsonText = TxtBatchScriptJson?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                MessageBox.Show("Vui lòng nhập nội dung JSON kịch bản!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var parsed = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);
                if (parsed != null && parsed.scenes != null && parsed.scenes.Count > 0)
                {
                    if (!string.IsNullOrWhiteSpace(parsed.video_title) && TxtVideoTitle != null)
                    {
                        TxtVideoTitle.Text = parsed.video_title;
                    }

                    BatchImageItems.Clear();
                    int idx = 1;
                    foreach (var s in parsed.scenes)
                    {
                        if (string.IsNullOrWhiteSpace(s.image_prompt)) continue;

                        var item = new BatchImageItem
                        {
                            Index = s.scene > 0 ? s.scene : idx,
                            SceneTitle = $"Cảnh {(s.scene > 0 ? s.scene : idx)}",
                            Transcript = s.transcript ?? string.Empty,
                            Prompt = s.image_prompt,
                            Status = "Waiting",
                            Engine = (CboxBatchEngine?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "flow",
                            Model = GetSelectedBatchModel(),
                            AspectRatio = GetSelectedBatchAspectRatio(),
                            Upscale = GetSelectedBatchUpscale()
                        };
                        BatchImageItems.Add(item);
                        idx++;
                    }

                    AutoDetectAndMatchExistingImages(TxtBatchOutputDir?.Text ?? string.Empty);

                    if (sender != this)
                    {
                        MessageBox.Show($"Đã cập nhật kịch bản thành công! Đã tải {BatchImageItems.Count} cảnh.", "Hoàn tất", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    if (sender != this)
                    {
                        MessageBox.Show("Không tìm thấy dữ liệu scenes hợp lệ trong JSON kịch bản.", "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                if (sender != this)
                {
                    MessageBox.Show($"Lỗi parse JSON kịch bản: {ex.Message}", "Lỗi Định Dạng JSON", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CboxBatchAspect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboxBatchAspect?.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString() ?? "16:9";
                if (RadAspect169 != null) RadAspect169.IsChecked = (tag == "16:9");
                if (RadAspect916 != null) RadAspect916.IsChecked = (tag == "9:16");
                if (RadAspect11 != null) RadAspect11.IsChecked = (tag == "1:1");
                if (RadAspect43 != null) RadAspect43.IsChecked = (tag == "4:3");
                if (RadAspect34 != null) RadAspect34.IsChecked = (tag == "3:4");

                foreach (var bItem in BatchImageItems)
                {
                    bItem.AspectRatio = tag;
                }
            }
        }

        private void BtnBatchViewToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ScrollCardGrid == null || BorderTableView == null || BtnBatchViewToggle == null) return;

            if (ScrollCardGrid.Visibility == Visibility.Visible)
            {
                ScrollCardGrid.Visibility = Visibility.Collapsed;
                BorderTableView.Visibility = Visibility.Visible;
                BtnBatchViewToggle.Content = "🖼️ Modern Card View";
            }
            else
            {
                ScrollCardGrid.Visibility = Visibility.Visible;
                BorderTableView.Visibility = Visibility.Collapsed;
                BtnBatchViewToggle.Content = "📋 Table Queue View";
            }
        }

        private void BtnBatchSelectChar_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Chọn ảnh nhân vật gốc (Reference Character Image)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string file in openFileDialog.FileNames)
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(file);
                        string ext = Path.GetExtension(file).TrimStart('.').ToLower();
                        if (ext == "jpg") ext = "jpeg";
                        string b64 = $"data:image/{ext};base64," + Convert.ToBase64String(bytes);

                        string cleanName = Path.GetFileNameWithoutExtension(file).Replace(" ", "_");
                        string tag = _batchRefImages.Count == 0 ? "@character" : $"@{cleanName}";

                        _batchRefImages.Add((b64, tag, file));
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error loading image {file}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                UpdateBatchRefImagesUI();
            }
        }

        private void BtnBatchClearChar_Click(object sender, RoutedEventArgs e)
        {
            _batchRefImages.Clear();
            UpdateBatchRefImagesUI();
        }

        private void UpdateBatchRefImagesUI()
        {
            if (_batchRefImages.Count == 0)
            {
                if (TxtBatchCharInfo != null) TxtBatchCharInfo.Text = "Chưa chọn ảnh nhân vật tham chiếu.";
                if (TxtBatchCharTag != null) TxtBatchCharTag.Text = "@character";
                if (PanelBatchCharEmpty != null) PanelBatchCharEmpty.Visibility = Visibility.Visible;
                if (PanelBatchCharHasImage != null) PanelBatchCharHasImage.Visibility = Visibility.Collapsed;
                if (ImgBatchCharPreview != null) ImgBatchCharPreview.Source = null;
            }
            else
            {
                if (TxtBatchCharInfo != null) TxtBatchCharInfo.Text = $"Đã tải {_batchRefImages.Count} ảnh: {Path.GetFileName(_batchRefImages[0].filePath)}";
                if (TxtBatchCharTag != null) TxtBatchCharTag.Text = string.Join(", ", _batchRefImages.Select(r => r.tag));
                if (PanelBatchCharEmpty != null) PanelBatchCharEmpty.Visibility = Visibility.Collapsed;
                if (PanelBatchCharHasImage != null) PanelBatchCharHasImage.Visibility = Visibility.Visible;

                if (ImgBatchCharPreview != null)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(_batchRefImages[0].filePath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();

                        ImgBatchCharPreview.Source = bitmap;
                    }
                    catch
                    {
                        ImgBatchCharPreview.Source = null;
                    }
                }
            }
        }

        private void CboxBatchEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CboxBatchEngine == null || PanelBatchFlowOptions == null) return;

            var selectedItem = CboxBatchEngine.SelectedItem as ComboBoxItem;
            string engine = selectedItem?.Tag?.ToString() ?? "flow";

            if (engine == "flow")
            {
                PanelBatchFlowOptions.Visibility = Visibility.Visible;
            }
            else
            {
                PanelBatchFlowOptions.Visibility = Visibility.Collapsed;
            }
        }

        private string GetSelectedBatchModel()
        {
            if (RadProviderFlowLocal?.IsChecked == true)
            {
                if (RadModelGemini30Pro?.IsChecked == true) return "gemini-3.0-pro-image";
                if (RadModelImagen4Preview?.IsChecked == true) return "imagen-4.0-generate-preview";
                if (RadModelNanoBanana2Flow?.IsChecked == true) return "nano-banana-2";
                if (RadModelNanoBananaProFlow?.IsChecked == true) return "nano-banana-pro";
                return "gemini-3.1-flash-image";
            }
            else
            {
                if (RadModelBananaPro?.IsChecked == true) return "nano_banana_pro";
                if (RadModelBananaLite?.IsChecked == true) return "nano_banana_2_lite";
                return "nano_banana_2";
            }
        }

        private string GetSelectedBatchUpscale()
        {
            if (RadUpscale2K?.IsChecked == true) return "2K";
            if (RadUpscale4K?.IsChecked == true) return "4K";
            return "none";
        }

        private string GetSelectedBatchAspectRatio()
        {
            if (CboxBatchAspect?.SelectedItem is ComboBoxItem selectedAspect && selectedAspect.Tag != null)
            {
                return selectedAspect.Tag.ToString() ?? "16:9";
            }
            if (RadAspect11?.IsChecked == true) return "1:1";
            if (RadAspect916?.IsChecked == true) return "9:16";
            if (RadAspect43?.IsChecked == true) return "4:3";
            if (RadAspect34?.IsChecked == true) return "3:4";
            return "16:9"; // Default 16:9
        }

        private void CboxBatchConcurrency_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateBatchConcurrencyUI();
        }

        private int UpdateBatchConcurrencyUI()
        {
            int count = 4;
            if (CboxBatchConcurrency?.SelectedItem is ComboBoxItem selectedConcurrency && int.TryParse(selectedConcurrency.Tag?.ToString(), out int parsedVal))
            {
                count = Math.Max(1, parsedVal);
            }

            if (BtnBatchGenerate != null)
            {
                BtnBatchGenerate.Content = $"⚡ Tạo hàng loạt ({count} ảnh song song)";
            }
            if (TxtBatchSubtitle != null)
            {
                TxtBatchSubtitle.Text = $"Tạo tối đa {count} ảnh song song với khoảng nghỉ 8 giây giữa các đợt.";
            }

            return count;
        }

        private void BtnBrowseBatchOutputDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Chọn thư mục lưu ảnh (Batch Images Output Directory)",
                InitialDirectory = string.IsNullOrWhiteSpace(TxtBatchOutputDir?.Text) ? AppDomain.CurrentDomain.BaseDirectory : TxtBatchOutputDir.Text
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName) && TxtBatchOutputDir != null)
            {
                TxtBatchOutputDir.Text = dialog.FolderName;
            }
        }

        private async void BtnBatchGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (BatchImageItems.Count == 0)
            {
                BtnUpdateScriptJson_Click(sender, e);
            }

            if (BatchImageItems.Count == 0)
            {
                MessageBox.Show("Chưa có cảnh nào để tạo! Vui lòng nhập JSON kịch bản và bấm 'Cập nhật kịch bản'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Confirmation check if no reference character image is selected
            if (_batchRefImages.Count == 0)
            {
                var confirmResult = MessageBox.Show(
                    "Chú ý: Bạn chưa chọn ảnh nhân vật tham chiếu.\n" +
                    "Các prompt chứa tag '@character' sẽ được sinh ảnh không có nhân vật gốc.\n\n" +
                    "Bạn có muốn tiếp tục sinh ảnh hàng loạt không?",
                    "Thiếu ảnh nhân vật gốc",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            string provider = RadProviderFlowLocal?.IsChecked == true ? "flow_local" : "glabs";

            string serverUrl;
            string apiKey;

            if (provider == "flow_local")
            {
                serverUrl = "http://127.0.0.1:8787/v1";
                apiKey = "flow-local-key";
            }
            else
            {
                serverUrl = ConfigService.CurrentSettings.ImageApiUrl;
                if (string.IsNullOrWhiteSpace(serverUrl)) serverUrl = "http://127.0.0.1:8765";
                apiKey = ConfigService.CurrentSettings.ImageApiKey;
            }

            string engine = (CboxBatchEngine?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "flow";
            string model = GetSelectedBatchModel();
            string aspectRatio = GetSelectedBatchAspectRatio();
            string upscale = GetSelectedBatchUpscale();

            int maxConcurrency = 4;
            if (CboxBatchConcurrency?.SelectedItem is ComboBoxItem selectedConcurrency && int.TryParse(selectedConcurrency.Tag?.ToString(), out int parsedVal))
            {
                maxConcurrency = Math.Max(1, parsedVal);
            }

            string outputDir = TxtBatchOutputDir?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
                if (TxtBatchOutputDir != null) TxtBatchOutputDir.Text = outputDir;
            }
            Directory.CreateDirectory(outputDir);

            BtnBatchGenerate.IsEnabled = false;

            // Reset item statuses
            foreach (var item in BatchImageItems)
            {
                item.Provider = provider;
                item.Engine = engine;
                item.Model = model;
                item.AspectRatio = aspectRatio;
                item.Upscale = upscale;
                item.Status = "Waiting";
                item.ErrorMessage = string.Empty;
                item.ImagePath = string.Empty;
            }

            // Clear Flow Local reference media ID cache for new batch run
            AssetAutomator.Services.Providers.FlowLocalImageGenProvider.ClearReferenceMediaCache();

            // Run processing in background with parallel concurrency control
            await Task.Run(async () =>
            {
                using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
                var tasks = BatchImageItems.Select(async item =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        await _batchImageGenService.ProcessSingleImageItemAsync(
                            item,
                            serverUrl,
                            apiKey,
                            _batchRefImages,
                            outputDir
                        );

                        // Save progress real-time on UI thread
                        _ = Dispatcher.InvokeAsync(async () =>
                        {
                            if (_activeProject != null)
                            {
                                await SaveCurrentProjectStateAsync();
                            }
                        });
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            });

            if (_activeProject != null)
            {
                await SaveCurrentProjectStateAsync();
            }

            BtnBatchGenerate.IsEnabled = true;
            MessageBox.Show($"Đã hoàn tất sinh {BatchImageItems.Count} ảnh cảnh hàng loạt!", "Hoàn thành", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CardImageContainer_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is BatchImageItem item && !string.IsNullOrEmpty(item.ImagePath))
            {
                if (File.Exists(item.ImagePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = item.ImagePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not open image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnBatchOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string outputDir = TxtBatchOutputDir?.Text ?? string.Empty;
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
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open output folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBatchOpenImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BatchImageItem item && !string.IsNullOrEmpty(item.ImagePath))
            {
                if (File.Exists(item.ImagePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = item.ImagePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not open image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("File ảnh không tồn tại trên ổ đĩa.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        #region Project Management & Card Actions

        private async Task LoadProjectsListAsync()
        {
            if (TxtProjectsStoragePath != null)
            {
                TxtProjectsStoragePath.Text = _batchProjectService.GetProjectsBaseDirectory();
            }

            var projects = await _batchProjectService.GetAllProjectsAsync();
            if (ItemsControlProjectsGrid != null)
            {
                ItemsControlProjectsGrid.ItemsSource = projects;
            }
        }

        private async void BtnBrowseProjectsDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Chọn Thư Mục Lưu Trữ Projects",
                InitialDirectory = _batchProjectService.GetProjectsBaseDirectory()
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                ConfigService.CurrentSettings.ProjectsStorageDir = dialog.FolderName;
                ConfigService.SaveSettings(ConfigService.CurrentSettings);
                await LoadProjectsListAsync();
            }
        }

        private async void BtnOpenProjectCard_Click(object sender, RoutedEventArgs e)
        {
            BatchProjectModel? project = (sender as FrameworkElement)?.Tag as BatchProjectModel 
                                         ?? (sender as FrameworkElement)?.DataContext as BatchProjectModel;

            if (project != null)
            {
                _activeProject = project;
                await LoadSelectedProjectDataAsync(project);

                if (PanelProjectsDashboard != null) PanelProjectsDashboard.Visibility = Visibility.Collapsed;
                if (PanelProjectEditor != null) PanelProjectEditor.Visibility = Visibility.Visible;
            }
        }

        private async void BtnBackToProjects_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProject != null)
            {
                await SaveCurrentProjectStateAsync();
            }

            await LoadProjectsListAsync();

            if (PanelProjectsDashboard != null) PanelProjectsDashboard.Visibility = Visibility.Visible;
            if (PanelProjectEditor != null) PanelProjectEditor.Visibility = Visibility.Collapsed;
        }

        private async void BtnDeleteProjectCard_Click(object sender, RoutedEventArgs e)
        {
            BatchProjectModel? project = (sender as FrameworkElement)?.Tag as BatchProjectModel 
                                         ?? (sender as FrameworkElement)?.DataContext as BatchProjectModel;

            if (project != null)
            {
                var confirm = MessageBox.Show($"Bạn có chắc chắn muốn xóa dự án '{project.ProjectName}' và toàn bộ kịch bản/ảnh liên quan không?", 
                                              "Xác nhận xóa dự án", 
                                              MessageBoxButton.YesNo, 
                                              MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    _batchProjectService.DeleteProject(project.ProjectName);
                    if (_activeProject?.ProjectId == project.ProjectId)
                    {
                        _activeProject = null;
                    }
                    await LoadProjectsListAsync();
                }
            }
        }

        private async Task LoadSelectedProjectDataAsync(BatchProjectModel project)
        {
            if (project == null) return;

            if (TxtVideoTitle != null) TxtVideoTitle.Text = project.ProjectName;
            if (TxtBatchOutputDir != null) TxtBatchOutputDir.Text = project.OutputDir;
            if (TxtBatchScriptJson != null && !string.IsNullOrWhiteSpace(project.ScriptJson))
            {
                TxtBatchScriptJson.Text = project.ScriptJson;
            }

            // Restore reference character image if saved
            _batchRefImages.Clear();
            if (project.RefImagePaths != null && project.RefImagePaths.Count > 0)
            {
                foreach (var path in project.RefImagePaths)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(path);
                            string ext = Path.GetExtension(path).TrimStart('.').ToLower();
                            if (ext == "jpg") ext = "jpeg";
                            string b64 = $"data:image/{ext};base64," + Convert.ToBase64String(bytes);

                            string cleanName = Path.GetFileNameWithoutExtension(path).Replace(" ", "_");
                            string tag = _batchRefImages.Count == 0 ? "@character" : $"@{cleanName}";

                            _batchRefImages.Add((b64, tag, path));
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[LoadRefImages] Error: {ex.Message}");
                        }
                    }
                }
            }
            UpdateBatchRefImagesUI();

            // Set aspect ratio
            if (CboxBatchAspect != null && !string.IsNullOrEmpty(project.AspectRatio))
            {
                foreach (ComboBoxItem item in CboxBatchAspect.Items)
                {
                    if (item.Tag?.ToString() == project.AspectRatio)
                    {
                        CboxBatchAspect.SelectedItem = item;
                        break;
                    }
                }
            }

            // Load items if saved in project
            if (project.Items != null && project.Items.Count > 0)
            {
                BatchImageItems.Clear();
                foreach (var state in project.Items)
                {
                    BatchImageItems.Add(new BatchImageItem
                    {
                        Index = state.Index,
                        SceneTitle = state.SceneTitle,
                        Transcript = state.Transcript,
                        Prompt = state.Prompt,
                        Status = state.Status,
                        ImagePath = state.ImagePath,
                        ErrorMessage = state.ErrorMessage,
                        MediaId = state.MediaId,
                        ReferenceMediaId = state.ReferenceMediaId,
                        Engine = state.Engine,
                        Model = state.Model,
                        AspectRatio = state.AspectRatio,
                        Upscale = state.Upscale
                    });
                }
            }
            else
            {
                BtnUpdateScriptJson_Click(this, new RoutedEventArgs());
            }

            // Auto-detect and link any existing generated images on disk
            AutoDetectAndMatchExistingImages(project.OutputDir);
            await SaveCurrentProjectStateAsync();
        }

        private void AutoDetectAndMatchExistingImages(string outputDir)
        {
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir)) return;

            try
            {
                var files = Directory.GetFiles(outputDir, "*.png")
                    .Concat(Directory.GetFiles(outputDir, "*.jpg"))
                    .Concat(Directory.GetFiles(outputDir, "*.webp"))
                    .ToList();

                if (files.Count == 0) return;

                foreach (var item in BatchImageItems)
                {
                    if (!string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
                    {
                        item.Status = "Done";
                        continue;
                    }

                    string p1 = $"_{item.Index}_";
                    string p2 = $"_{item.Index}.";

                    var matchedFile = files.LastOrDefault(f =>
                    {
                        string name = Path.GetFileName(f);
                        return name.Contains(p1, StringComparison.OrdinalIgnoreCase) ||
                               name.Contains(p2, StringComparison.OrdinalIgnoreCase);
                    });

                    if (!string.IsNullOrEmpty(matchedFile))
                    {
                        item.ImagePath = matchedFile;
                        item.Status = "Done";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoDetectImages] Error matching files: {ex.Message}");
            }
        }

        private async void BtnNewProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewProjectWindow();
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ProjectName))
            {
                string initialScript = TxtBatchScriptJson?.Text ?? string.Empty;
                var newProj = await _batchProjectService.CreateProjectAsync(dialog.ProjectName, initialScript);
                _activeProject = newProj;
                await LoadSelectedProjectDataAsync(newProj);
                await LoadProjectsListAsync();

                if (PanelProjectsDashboard != null) PanelProjectsDashboard.Visibility = Visibility.Collapsed;
                if (PanelProjectEditor != null) PanelProjectEditor.Visibility = Visibility.Visible;

                MessageBox.Show($"Đã khởi tạo dự án '{newProj.ProjectName}' thành công!", "Dự án mới", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BtnSaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (_activeProject == null)
            {
                BtnNewProject_Click(sender, e);
                return;
            }

            await SaveCurrentProjectStateAsync();
            MessageBox.Show($"Đã lưu dự án '{_activeProject.ProjectName}' thành công!", "Đã lưu", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task SaveCurrentProjectStateAsync()
        {
            if (_activeProject == null) return;

            _activeProject.ScriptJson = TxtBatchScriptJson?.Text ?? string.Empty;
            _activeProject.OutputDir = TxtBatchOutputDir?.Text ?? string.Empty;
            _activeProject.AspectRatio = GetSelectedBatchAspectRatio();
            _activeProject.Model = GetSelectedBatchModel();
            _activeProject.Engine = (CboxBatchEngine?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "flow";
            _activeProject.Provider = RadProviderFlowLocal?.IsChecked == true ? "flow_local" : "glabs";

            _activeProject.RefImagePaths = _batchRefImages
                .Select(r => r.filePath)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .ToList();

            _activeProject.Items = BatchImageItems.Select(item => new BatchImageItemState
            {
                Index = item.Index,
                SceneTitle = item.SceneTitle,
                Transcript = item.Transcript,
                Prompt = item.Prompt,
                Status = item.Status,
                ImagePath = item.ImagePath,
                ErrorMessage = item.ErrorMessage,
                MediaId = item.MediaId,
                ReferenceMediaId = item.ReferenceMediaId,
                Engine = item.Engine,
                Model = item.Model,
                AspectRatio = item.AspectRatio,
                Upscale = item.Upscale
            }).ToList();

            await _batchProjectService.SaveProjectAsync(_activeProject);
        }

        private async void BtnCardRegenerate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is BatchImageItem item)
            {
                if (item.IsGenerating)
                {
                    MessageBox.Show("Cảnh này đang trong quá trình sinh ảnh!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string provider = RadProviderFlowLocal?.IsChecked == true ? "flow_local" : "glabs";
                string serverUrl;
                string apiKey;

                if (provider == "flow_local")
                {
                    serverUrl = "http://127.0.0.1:8787/v1";
                    apiKey = "flow-local-key";
                }
                else
                {
                    serverUrl = ConfigService.CurrentSettings.ImageApiUrl;
                    if (string.IsNullOrWhiteSpace(serverUrl)) serverUrl = "http://127.0.0.1:8765";
                    apiKey = ConfigService.CurrentSettings.ImageApiKey;
                }

                string engine = (CboxBatchEngine?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "flow";
                string model = GetSelectedBatchModel();
                string aspectRatio = GetSelectedBatchAspectRatio();
                string upscale = GetSelectedBatchUpscale();

                string outputDir = TxtBatchOutputDir?.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(outputDir))
                {
                    outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
                }
                Directory.CreateDirectory(outputDir);

                item.Provider = provider;
                item.Engine = engine;
                item.Model = model;
                item.AspectRatio = aspectRatio;
                item.Upscale = upscale;
                item.Status = "Generating...";
                item.ErrorMessage = string.Empty;

                await Task.Run(async () =>
                {
                    await _batchImageGenService.ProcessSingleImageItemAsync(
                        item,
                        serverUrl,
                        apiKey,
                        _batchRefImages,
                        outputDir
                    );
                });

                if (_activeProject != null)
                {
                    await SaveCurrentProjectStateAsync();
                }
            }
        }

        private void BtnCardCopyPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is BatchImageItem item && !string.IsNullOrWhiteSpace(item.Prompt))
            {
                Clipboard.SetText(item.Prompt);
                MessageBox.Show("Đã sao chép prompt của cảnh vào Clipboard!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion
    }
}
