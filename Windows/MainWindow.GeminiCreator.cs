using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AssetAutomator.Services;

namespace AssetAutomator
{
    public partial class MainWindow : Window
    {
        private GeminiApiService? _geminiApiService;
        private GeminiVideoPipelineService? _geminiVideoPipelineService;

        private void InitializeGeminiCreatorServices()
        {
            _geminiApiService ??= new GeminiApiService();

            // Lazy initialize step services for Gemini Pipeline
            var batchImageGenService = new BatchImageGenService();
            var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);
            var voiceoverStep = new VoiceoverGenerationStep();
            var sceneBreakdownStep = new GeminiSceneBreakdownStep(_geminiApiService);
            var imageBatchStep = new SceneImageBatchStep(batchImageGenService);

            _geminiVideoPipelineService = new GeminiVideoPipelineService(
                topicResearchStep,
                voiceoverStep,
                sceneBreakdownStep,
                imageBatchStep
            );

            // Load gems on initial setup
            _ = LoadGeminiGemsToComboboxesAsync();
        }

        private async Task LoadGeminiGemsToComboboxesAsync()
        {
            try
            {
                _geminiApiService ??= new GeminiApiService();
                var gems = await _geminiApiService.GetGemsAsync(includeHidden: true);

                Dispatcher.Invoke(() =>
                {
                    CboScriptwriterGem.Items.Clear();
                    CboSceneCreatorGem.Items.Clear();

                    // Default option
                    CboScriptwriterGem.Items.Add(new ComboBoxItem { Content = "-- Gemini Mặc Định --", Tag = string.Empty });
                    CboSceneCreatorGem.Items.Add(new ComboBoxItem { Content = "-- Gemini Mặc Định --", Tag = string.Empty });

                    int scriptwriterIdx = 0;
                    int sceneCreatorIdx = 0;

                    // Filter only Custom Gems (predefined == false)
                    var customGems = gems.Where(g => !g.predefined).ToList();

                    for (int i = 0; i < customGems.Count; i++)
                    {
                        var gem = customGems[i];
                        string displayName = gem.name;

                        var item1 = new ComboBoxItem { Content = displayName, Tag = gem.id };
                        var item2 = new ComboBoxItem { Content = displayName, Tag = gem.id };

                        CboScriptwriterGem.Items.Add(item1);
                        CboSceneCreatorGem.Items.Add(item2);

                        if (gem.id.Equals(ConfigService.CurrentSettings.ScriptwriterGemId, StringComparison.OrdinalIgnoreCase))
                        {
                            scriptwriterIdx = i + 1;
                        }
                        if (gem.id.Equals(ConfigService.CurrentSettings.SceneCreatorGemId, StringComparison.OrdinalIgnoreCase))
                        {
                            sceneCreatorIdx = i + 1;
                        }
                    }

                    CboScriptwriterGem.SelectedIndex = scriptwriterIdx;
                    CboSceneCreatorGem.SelectedIndex = sceneCreatorIdx;

                    LogGeminiMessage($"[SUCCESS] Đã làm mới danh sách Gems! Đã nạp {customGems.Count} Custom Gems.", Colors.Green);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LogGeminiMessage($"[ERROR] Không thể tải danh sách Gemini Gems: {ex.Message}", Colors.Red);
                });
            }
        }

        private async void BtnRefreshGems_Click(object sender, RoutedEventArgs e)
        {
            LogGeminiMessage("[INFO] Đang tải lại danh sách Gemini Gems...", Colors.DodgerBlue);
            await LoadGeminiGemsToComboboxesAsync();
        }

        private async void BtnRunGeminiWorkflow_Click(object sender, RoutedEventArgs e)
        {
            string topic = TxtGeminiTopic.Text.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                MessageBox.Show("Vui lòng nhập Chủ đề hoặc Link Channel YouTube!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            InitializeGeminiCreatorServices();

            string scriptwriterGemId = (CboScriptwriterGem.SelectedItem is ComboBoxItem item1 && item1.Tag != null) ? item1.Tag.ToString() ?? string.Empty : string.Empty;
            string sceneCreatorGemId = (CboSceneCreatorGem.SelectedItem is ComboBoxItem item2 && item2.Tag != null) ? item2.Tag.ToString() ?? string.Empty : string.Empty;
            string voiceId = TxtGeminiVoiceId.Text.Trim();
            bool enableDeepResearch = ChkGeminiDeepResearch.IsChecked == true;
            string providerKey = (CboGeminiImageProvider.SelectedItem is ComboBoxItem item3) ? item3.Content.ToString() ?? "flow_local" : "flow_local";

            BtnRunGeminiWorkflow.IsEnabled = false;
            TxtGeminiLog.Document.Blocks.Clear();

            var task = new AutomationTask
            {
                VideoUrl = topic,
                VoiceId = voiceId,
                TargetLanguage = "vi",
                Step1 = false,
                Step2 = true,  // Deep Research Transcript
                Step3 = true,  // Scene Breakdown
                Step4 = true,  // Voiceover
                Step5 = true,  // Batch Image Gen
                StepSrt = true
            };

            LogGeminiMessage($"[GEMINI-WORKFLOW] 🚀 Khởi chạy Workflow Gemini 5 Bước cho topic: '{topic}'", Colors.Gold);

            try
            {
                await Task.Run(async () =>
                {
                    await _geminiVideoPipelineService!.ExecutePipelineAsync(
                        task: task,
                        topicOrUrl: topic,
                        scriptwriterGemId: scriptwriterGemId,
                        sceneCreatorGemId: sceneCreatorGemId,
                        enableDeepResearch: enableDeepResearch,
                        voiceId: voiceId,
                        imageGenProvider: providerKey,
                        logTask: (t, msg) => Dispatcher.Invoke(() => LogGeminiMessage(msg, GetLogColor(msg)))
                    );
                });

                MessageBox.Show("Đã hoàn thành Workflow Gemini 5 Bước!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogGeminiMessage($"[ERROR] Lỗi thực thi Pipeline: {ex.Message}", Colors.Red);
                MessageBox.Show($"Xảy ra lỗi trong quá trình thực thi: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRunGeminiWorkflow.IsEnabled = true;
            }
        }

        private void BtnViewScenesJson_Click(object sender, RoutedEventArgs e)
        {
            string topic = TxtGeminiTopic.Text.Trim();
            string videoId = YoutubeHelper.ExtractVideoId(topic);
            string outputDir = YoutubeHelper.GetOutputDir(videoId);
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            if (!File.Exists(scenesPath))
            {
                MessageBox.Show($"Không tìm thấy file scenes.json tại: {scenesPath}.\nVui lòng chạy Workflow Step 4 trước.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string jsonText = File.ReadAllText(scenesPath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonText, options);

                if (rootData != null)
                {
                    var viewerWin = new Windows.ScenesViewerWindow(rootData);
                    viewerWin.Owner = this;
                    viewerWin.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không thể đọc scenes.json: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogGeminiMessage(string message, Color color)
        {
            var paragraph = new Paragraph();
            paragraph.Margin = new Thickness(0, 2, 0, 2);
            var run = new Run($"[{DateTime.Now:HH:mm:ss}] {message}")
            {
                Foreground = new SolidColorBrush(color)
            };
            paragraph.Inlines.Add(run);

            TxtGeminiLog.Document.Blocks.Add(paragraph);
            TxtGeminiLog.ScrollToEnd();
        }

        private Color GetLogColor(string msg)
        {
            if (msg.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)) return Colors.Red;
            if (msg.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase)) return Colors.Orange;
            if (msg.Contains("Success", StringComparison.OrdinalIgnoreCase) || msg.Contains("🎉", StringComparison.OrdinalIgnoreCase)) return Colors.LightGreen;
            if (msg.Contains("[STEP", StringComparison.OrdinalIgnoreCase)) return Colors.Cyan;
            return (Color)ColorConverter.ConvertFromString("#CCCCCC");
        }
    }
}
