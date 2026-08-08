using System;
using System.IO;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AssetAutomator.WinUI.Views.Dialogs;

/// <summary>
/// Single tabbed viewer that shows the three task-output assets side-by-side:
///   • scenes.json      (pretty-printed if valid)
///   • transcript.txt   (plain text)
///   • voiceover.srt    (plain text, SRT subtitle format)
///
/// Replaces the previous pair of single-purpose popups (JsonViewerDialog,
/// TextViewerDialog). When a file is missing on disk, the matching tab shows
/// a friendly placeholder that explains where the user would have found it.
/// </summary>
public sealed partial class AssetViewerDialog : ContentDialog
{
    /// <summary>Width multiplier vs the host window — 50% of the screen.</summary>
    private const double ScreenWidthRatio = 0.5;

    /// <summary>The currently visible tab — drives Copy + which content we read.</summary>
    private enum AssetTab { Scenes, Transcript, Srt }

    private AssetTab _currentTab = AssetTab.Scenes;

    private string _scenesPath = string.Empty;
    private string _transcriptPath = string.Empty;
    private string _srtPath = string.Empty;

    private string _scenesContent = string.Empty;
    private string _transcriptContent = string.Empty;
    private string _srtContent = string.Empty;

    /// <summary>
    /// Build a viewer for the three assets of one task.
    /// <paramref name="outputDir"/> is the task's output folder; each file
    /// inside it is read independently. Missing files get a helpful placeholder.
    /// </summary>
    public AssetViewerDialog(string? outputDir, string? fallbackScenesContent = null)
    {
        InitializeComponent();

        string resolvedDir = outputDir ?? string.Empty;

        // Resolve the three file paths (might or might not exist on disk)
        _scenesPath = Path.Combine(resolvedDir, "scenes.json");
        _transcriptPath = Path.Combine(resolvedDir, "transcript.txt");
        _srtPath = Path.Combine(resolvedDir, "voiceover.srt");

        _scenesContent = LoadScenes(_scenesPath, fallbackScenesContent);
        _transcriptContent = LoadOrFallback(
            _transcriptPath,
            "(Chưa có file transcript.txt cho task này.)\n\n" +
            $"• Thư mục xuất: {resolvedDir}\n" +
            $"• Đường dẫn dự kiến: {_transcriptPath}\n\n" +
            "Transcript được tạo tự động ở Step 1 (Deep Research) của pipeline Gemini.\n" +
            "Hãy chạy task trước rồi bấm 'View Assets' lại.");
        _srtContent = LoadOrFallback(
            _srtPath,
            "(Chưa có file voiceover.srt cho task này.)\n\n" +
            $"• Thư mục xuất: {resolvedDir}\n" +
            $"• Đường dẫn dự kiến: {_srtPath}\n\n" +
            "voiceover.srt được tạo tự động ở Step 3 (Text-to-Speech → Whisper SRT) của pipeline Gemini.\n" +
            "Hãy chạy task trước rồi bấm 'View Assets' lại.");

        TxtScenesPath.Text = $"📂 {_scenesPath}";
        TxtScenesStats.Text = BuildStats(_scenesContent, includeWordCount: false);
        TxtScenesContent.Text = _scenesContent;

        TxtTranscriptPath.Text = $"📄 {_transcriptPath}";
        TxtTranscriptStats.Text = BuildStats(_transcriptContent, includeWordCount: true);
        TxtTranscriptContent.Text = _transcriptContent;

        TxtSrtPath.Text = $"📄 {_srtPath}";
        TxtSrtStats.Text = BuildStats(_srtContent, includeWordCount: false);
        TxtSrtContent.Text = _srtContent;

        // Track which tab is visible so the Copy button copies the right one
        Tabs.SelectionChanged += (_, __) => UpdateCurrentTabFromSelection();

        // Match the same responsive sizing trick used by VoiceSelectorDialog:
        // override the ContentDialogMaxWidth/MinWidth template resources.
        Loaded += (s, e) => ApplyScreenSizedLayout();
        SizeChanged += (s, e) => ApplyScreenSizedLayout();
    }

    /// <summary>
    /// Sizes the dialog to 50% of the current display area (workArea).
    /// Method is idempotent — safe to call multiple times.
    /// </summary>
    public void ApplyScreenSizedLayout()
    {
        try
        {
            this.HorizontalAlignment = HorizontalAlignment.Center;
            this.VerticalAlignment = VerticalAlignment.Center;
            this.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            this.VerticalContentAlignment = VerticalAlignment.Stretch;

            if (DialogRootGrid != null)
            {
                DialogRootGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                DialogRootGrid.VerticalAlignment = VerticalAlignment.Stretch;
                DialogRootGrid.Width = double.NaN;
            }

            double targetWidth = 900;
            if (App.MainWindowInstance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    windowId,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    targetWidth = Math.Max(800, displayArea.WorkArea.Width * ScreenWidthRatio);
                }
            }

            this.Resources["ContentDialogMaxWidth"] = targetWidth;
            this.Resources["ContentDialogMinWidth"] = targetWidth;

            DialogRootGrid?.InvalidateMeasure();
            DialogRootGrid?.InvalidateArrange();
            this.UpdateLayout();
        }
        catch
        {
            // Even if the screen lookup fails, leave the default sizing so
            // the dialog is still usable.
        }
    }

    private void UpdateCurrentTabFromSelection()
    {
        // Reference equality — these PivotItem instances are field-named, so
        // comparing the SelectedItem reference is the cleanest way.
        if (ReferenceEquals(Tabs.SelectedItem, TabTranscript)) _currentTab = AssetTab.Transcript;
        else if (ReferenceEquals(Tabs.SelectedItem, TabSrt)) _currentTab = AssetTab.Srt;
        else _currentTab = AssetTab.Scenes;
    }

    private void BtnCopyCurrent_Click(object sender, RoutedEventArgs e)
    {
        string toCopy = _currentTab switch
        {
            AssetTab.Transcript => _transcriptContent,
            AssetTab.Srt => _srtContent,
            _ => _scenesContent,
        };
        if (string.IsNullOrEmpty(toCopy)) return;
        try
        {
            var package = new DataPackage();
            package.SetText(toCopy);
            Clipboard.SetContent(package);
        }
        catch
        {
            // Clipboard access can fail in rare scenarios; not critical.
        }
    }

    // ─────────────────────────────────────────────────────
    //  Loaders
    // ─────────────────────────────────────────────────────

    private static string LoadScenes(string path, string? fallbackContent)
    {
        // 1. file on disk?
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                string raw = File.ReadAllText(path);
                return TryPrettyPrintJson(raw) ?? raw;
            }
            catch (Exception ex)
            {
                return $"// Không thể đọc file: {ex.Message}";
            }
        }
        // 2. caller supplied in-memory JSON (e.g. the current TextBox content)
        if (!string.IsNullOrWhiteSpace(fallbackContent))
        {
            return TryPrettyPrintJson(fallbackContent) ?? fallbackContent;
        }
        // 3. nothing → friendly placeholder
        return "(Chưa có file scenes.json cho task này.)\n\n" +
               $"• Đường dẫn dự kiến: {path}\n\n" +
               "scenes.json được tạo tự động ở Step 4 (Scene Breakdown) của pipeline Gemini.\n" +
               "Hãy chạy task trước rồi bấm 'View Assets' lại.";
    }

    private static string LoadOrFallback(string path, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return $"// Không thể đọc file: {ex.Message}";
            }
        }
        return fallback;
    }

    private static string? TryPrettyPrintJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            return null; // not valid JSON — show raw text as-is
        }
    }

    private static string BuildStats(string content, bool includeWordCount)
    {
        if (string.IsNullOrEmpty(content))
        {
            return includeWordCount ? "0 ký tự • 0 dòng • 0 từ" : "0 ký tự • 0 dòng";
        }
        int lineCount = content.Split('\n').Length;
        if (includeWordCount)
        {
            int wordCount = content.Split(
                new[] { ' ', '\t', '\n', '\r' },
                StringSplitOptions.RemoveEmptyEntries).Length;
            return $"{content.Length:N0} ký tự • {lineCount:N0} dòng • {wordCount:N0} từ";
        }
        return $"{content.Length:N0} ký tự • {lineCount:N0} dòng";
    }
}