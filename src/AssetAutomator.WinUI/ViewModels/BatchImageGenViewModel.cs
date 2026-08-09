using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Services.Providers;
using Microsoft.UI.Xaml;

namespace AssetAutomator.WinUI.ViewModels;

    public partial class BatchImageGenViewModel : ObservableObject
    {
        private readonly BatchProjectService? _batchProjectService;
        private readonly BatchImageGenService? _batchImageGenService;
        private readonly IConfigService? _configService;
        private readonly AssetAutomator.Application.Services.HistoryService? _historyService;
        private readonly AssetAutomator.Application.Services.WatermarkRemovalQueue? _watermarkQueue;
        private readonly AssetAutomator.Core.Interfaces.IWatermarkRemover? _watermarkRemover;

        private readonly List<(string base64Data, string tag, string filePath)> _batchRefImages = new();

        [ObservableProperty]
        private string _projectsStoragePath = string.Empty;

        [ObservableProperty]
        private ObservableCollection<BatchProjectModel> _projects = new();

        [ObservableProperty]
        private BatchProjectModel? _activeProject;

        [ObservableProperty]
        private bool _isDashboardVisible = true;

    [ObservableProperty]
    private bool _isEditorVisible = false;

    public Microsoft.UI.Xaml.Visibility DashboardVisibility => IsDashboardVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility EditorVisibility => IsEditorVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnIsDashboardVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(DashboardVisibility));
    }

    partial void OnIsEditorVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(EditorVisibility));
    }

    [ObservableProperty]
    private bool _isTableViewVisible = false;

    public Microsoft.UI.Xaml.Visibility CardGridVisibility => IsTableViewVisible ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility TableViewVisibility => IsTableViewVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public string ViewToggleText => IsTableViewVisible ? "🖼️ Cards Grid View" : "📋 Table Queue View";

    partial void OnIsTableViewVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(CardGridVisibility));
        OnPropertyChanged(nameof(TableViewVisibility));
        OnPropertyChanged(nameof(ViewToggleText));
    }

    // Editor View properties
    [ObservableProperty]
    private string _projectTitle = "Bedtime Psychology";

    [ObservableProperty]
    private string _outputDir = string.Empty;

    [ObservableProperty]
    private string _scriptJson = string.Empty;

    [ObservableProperty]
    private ObservableCollection<BatchImageItem> _batchImageItems = new();

    // Reference Character Image UI properties
    [ObservableProperty]
    private string _refImageInfo = "Chưa chọn ảnh nhân vật tham chiếu.";

    [ObservableProperty]
    private string _refImageTag = "@character";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyRefImageVisibility))]
    [NotifyPropertyChangedFor(nameof(HasRefImageVisibility))]
    private bool _hasRefImage = false;

    [ObservableProperty]
    private string _refPreviewImagePath = string.Empty;

    public Microsoft.UI.Xaml.Visibility EmptyRefImageVisibility => HasRefImage ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public Microsoft.UI.Xaml.Visibility HasRefImageVisibility => HasRefImage ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    // Config & Engine properties (Google Flow Local is the only provider).
    [ObservableProperty]
    private string _selectedAspect = "16:9";

    [ObservableProperty]
    private string _selectedEngine = "flow";

    /// <summary>Always <c>flow_local</c>. Kept for backward-compat binding.</summary>
    [ObservableProperty]
    private string _selectedProvider = "flow_local";

    [ObservableProperty]
    private string _selectedModel = "nano-banana-2";

    [ObservableProperty]
    private string _selectedUpscale = "none";

    [ObservableProperty]
    private int _selectedConcurrency = 4;

    [ObservableProperty]
    private double _totalDoneCount = 0;

    [ObservableProperty]
    private double _totalCount = 0;

    [ObservableProperty]
    private int _progressPercent = 0;

    [ObservableProperty]
    private string _progressText = "Đã tạo 0/0 ảnh (0%)";

    /// <summary>
    /// Transient status message (e.g. errors from the watermark pipeline that
    /// don't fit into a per-item <see cref="BatchImageItem.WatermarkNote"/>).
    /// Bound to the editor's status bar; cleared by the next mutation.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isGenerating = false;

    /// <summary>
    /// True while there is at least one <c>Done</c> item with an image on disk
    /// that could benefit from watermark removal. Used by the XAML button to
    /// enable/disable itself based on context.
    /// </summary>
    [ObservableProperty]
    private bool _hasDoneItems = false;

    public string GenerateButtonText => $"⚡ Tạo hàng loạt ({SelectedConcurrency} ảnh song song)";

    public Microsoft.UI.Xaml.Visibility FlowOptionsVisibility => SelectedEngine == "flow" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnSelectedEngineChanged(string value)
    {
        OnPropertyChanged(nameof(FlowOptionsVisibility));
    }

    partial void OnSelectedConcurrencyChanged(int value)
    {
        OnPropertyChanged(nameof(GenerateButtonText));
    }

    public BatchImageGenViewModel(
        BatchProjectService? batchProjectService = null,
        BatchImageGenService? batchImageGenService = null,
        IConfigService? configService = null,
        AssetAutomator.Application.Services.HistoryService? historyService = null,
        AssetAutomator.Application.Services.WatermarkRemovalQueue? watermarkQueue = null,
        AssetAutomator.Core.Interfaces.IWatermarkRemover? watermarkRemover = null)
    {
        _batchProjectService = batchProjectService;
        _batchImageGenService = batchImageGenService;
        _configService = configService;
        _historyService = historyService;
        _watermarkQueue = watermarkQueue;
        _watermarkRemover = watermarkRemover;

        ScriptJson = GetDefaultScriptJson();
        OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");

        _ = LoadProjectsListAsync();

        // Subscribe to pipeline completion events so the dashboard auto-refreshes
        // when a BatchProject is created/updated by the Gemini pipeline, even if
        // the user is currently looking at the Gemini tab. The handler is cheap
        // (just schedules a fire-and-forget reload), and we never unsubscribe
        // because BatchImageGenViewModel is registered as a singleton for the
        // lifetime of the WinUI process.
        PipelineEvents.BatchProjectUpdated += OnPipelineProjectUpdated;
    }

    private void OnPipelineProjectUpdated(object? sender, BatchProjectUpdatedEventArgs e)
    {
        // Marshal to the UI thread before touching ObservableCollection. The
        // pipeline publishes from a worker thread; if the user happens to be
        // on the Batch Image Gen tab when the event fires, the dashboard must
        // update safely.
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            ?? App.MainWindowInstance?.DispatcherQueue;

        void Reload()
        {
            try
            {
                _ = LoadProjectsListAsync();
            }
            catch
            {
                // Never crash the dashboard from a background event.
            }
        }

        if (dispatcher != null && dispatcher.HasThreadAccess)
        {
            Reload();
        }
        else
        {
            dispatcher?.TryEnqueue(() => Reload());
        }
    }

    [RelayCommand]
    public async Task LoadProjectsListAsync()
    {
        if (_batchProjectService == null) return;

        try
        {
            ProjectsStoragePath = _batchProjectService.GetProjectsBaseDirectory();
            var list = await _batchProjectService.GetAllProjectsAsync();

            Projects.Clear();
            foreach (var proj in list)
            {
                Projects.Add(proj);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadProjectsListAsync] Failed: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task OpenProjectAsync(BatchProjectModel? project)
    {
        if (project == null) return;

        ActiveProject = project;
        ProjectTitle = project.ProjectName;
        OutputDir = string.IsNullOrWhiteSpace(project.OutputDir)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages")
            : project.OutputDir;

        ScriptJson = string.IsNullOrWhiteSpace(project.ScriptJson) ? GetDefaultScriptJson() : project.ScriptJson;
        SelectedAspect = string.IsNullOrWhiteSpace(project.AspectRatio) ? "16:9" : project.AspectRatio;
        SelectedEngine = string.IsNullOrWhiteSpace(project.Engine) ? "flow" : project.Engine;
        // Legacy projects may have provider="glabs". Normalize silently.
        SelectedProvider = ImageGenProviderFactory.FlowLocalProviderKey;
        SelectedModel = string.IsNullOrWhiteSpace(project.Model) ? "nano-banana-2" : project.Model;
        SelectedUpscale = string.IsNullOrWhiteSpace(project.Upscale) ? "none" : project.Upscale;
        SelectedConcurrency = project.Concurrency > 0 ? project.Concurrency : 4;

        // Restore reference character image if saved
        ClearRefImages();
        if (project.RefImagePaths != null && project.RefImagePaths.Count > 0)
        {
            foreach (var path in project.RefImagePaths)
            {
                if (File.Exists(path))
                {
                    AddRefImage(path);
                }
            }
        }

        BatchImageItems.Clear();
        if (project.Items != null && project.Items.Count > 0)
        {
            foreach (var state in project.Items)
            {
                BatchImageItems.Add(new BatchImageItem
                {
                    Index = state.Index,
                    // Normalize here so projects saved with the old format
                    // ("Scene #1: scene_001") display the new compact "001" badge
                    // without needing a project migration. New pipeline runs
                    // already write the compact format directly.
                    SceneTitle = BatchImageItem.NormalizeSceneTitle(state.SceneTitle, state.Index),
                    Transcript = state.Transcript,
                    Prompt = state.Prompt,
                    Status = state.Status,
                    ImagePath = state.ImagePath,
                    ErrorMessage = state.ErrorMessage,
                    MediaId = state.MediaId,
                    ReferenceMediaId = state.ReferenceMediaId,
                    FlowProjectId = state.FlowProjectId ?? project.FlowProjectId,
                    FlowProjectTitle = state.FlowProjectTitle ?? project.ProjectName,
                    FlowProjectUrl = state.FlowProjectUrl ?? project.FlowProjectUrl,
                    Engine = state.Engine,
                    Model = state.Model,
                    AspectRatio = state.AspectRatio,
                    Upscale = state.Upscale,
                    WatermarkRemoved = state.WatermarkRemoved,
                    WatermarkNote = state.WatermarkNote
                });
            }
        }
        else
        {
            UpdateScriptJson(showInfoMessage: false);
        }

        AutoDetectAndMatchExistingImages(OutputDir);
        UpdateProgressUI();

        IsDashboardVisible = false;
        IsEditorVisible = true;

        await SaveCurrentProjectStateAsync();
    }

    [RelayCommand]
    public void ToggleView()
    {
        IsTableViewVisible = !IsTableViewVisible;
    }

    [RelayCommand]
    public void OpenFlowProjectUrl()
    {
        string? url = ActiveProject?.FlowProjectUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            var item = BatchImageItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.FlowProjectUrl));
            url = item?.FlowProjectUrl;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://labs.google/fx/tools/flow";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    [RelayCommand]
    public async Task RegenerateSingleItemAsync(BatchImageItem? item)
    {
        if (item == null || IsGenerating) return;

        item.Status = "Waiting";
        item.ErrorMessage = string.Empty;
        item.ImagePath = string.Empty;

        // Google Flow Local API is the only image-gen backend now.
        string serverUrl = _configService?.CurrentSettings.ImageApiUrl ?? "http://127.0.0.1:8787/v1";
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            serverUrl = "http://127.0.0.1:8787/v1";
        }
        string apiKey = _configService?.CurrentSettings.ImageApiKey ?? "flow-local-key";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "flow-local-key";
        }

        string outputDir = OutputDir;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
            OutputDir = outputDir;
        }
        Directory.CreateDirectory(outputDir);

        if (_batchImageGenService != null)
        {
            await _batchImageGenService.ProcessSingleImageItemAsync(
                item,
                serverUrl,
                apiKey,
                _batchRefImages,
                outputDir
            );
        }

        UpdateProgressUI();
        if (ActiveProject != null)
        {
            await SaveCurrentProjectStateAsync();
        }
    }

    [RelayCommand]
    public async Task BackToProjectsAsync()
    {
        if (ActiveProject != null)
        {
            await SaveCurrentProjectStateAsync();
        }

        await LoadProjectsListAsync();

        IsDashboardVisible = true;
        IsEditorVisible = false;
    }

    [RelayCommand]
    public async Task DeleteProjectAsync(BatchProjectModel? project)
    {
        if (project == null) return;

        if (_batchProjectService != null)
        {
            _batchProjectService.DeleteProject(project.ProjectName);
            if (ActiveProject?.ProjectId == project.ProjectId)
            {
                ActiveProject = null;
            }
            await LoadProjectsListAsync();
        }
    }

    [RelayCommand]
    public async Task SaveCurrentProjectStateAsync()
    {
        if (ActiveProject == null || _batchProjectService == null) return;

        ActiveProject.ScriptJson = ScriptJson;
        ActiveProject.OutputDir = OutputDir;
        ActiveProject.AspectRatio = SelectedAspect;
        ActiveProject.Engine = SelectedEngine;
        ActiveProject.Provider = ImageGenProviderFactory.FlowLocalProviderKey;
        ActiveProject.Model = SelectedModel;
        ActiveProject.Upscale = SelectedUpscale;
        ActiveProject.Concurrency = SelectedConcurrency;

        ActiveProject.RefImagePaths = _batchRefImages
            .Select(r => r.filePath)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        var itemWithProj = BatchImageItems.FirstOrDefault(i => !string.IsNullOrEmpty(i.FlowProjectId));
        if (itemWithProj != null)
        {
            ActiveProject.FlowProjectId = itemWithProj.FlowProjectId;
            ActiveProject.FlowProjectUrl = itemWithProj.FlowProjectUrl;
        }

        ActiveProject.Items = BatchImageItems.Select(item => new BatchImageItemState
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
            FlowProjectId = item.FlowProjectId,
            FlowProjectTitle = item.FlowProjectTitle,
            FlowProjectUrl = item.FlowProjectUrl,
            Engine = item.Engine,
            Model = item.Model,
            AspectRatio = item.AspectRatio,
            Upscale = item.Upscale,
            WatermarkRemoved = item.WatermarkRemoved,
            WatermarkNote = item.WatermarkNote
        }).ToList();

        await _batchProjectService.SaveProjectAsync(ActiveProject);
    }

    public void AddRefImage(string filePath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            string ext = Path.GetExtension(filePath).TrimStart('.').ToLower();
            if (ext == "jpg") ext = "jpeg";
            string b64 = $"data:image/{ext};base64," + Convert.ToBase64String(bytes);

            string cleanName = Path.GetFileNameWithoutExtension(filePath).Replace(" ", "_");
            string tag = _batchRefImages.Count == 0 ? "@character" : $"@{cleanName}";

            _batchRefImages.Add((b64, tag, filePath));
            UpdateRefImagesUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AddRefImage] Error: {ex.Message}");
        }
    }

    public void ClearRefImages()
    {
        _batchRefImages.Clear();
        UpdateRefImagesUI();
    }

    private void UpdateRefImagesUI()
    {
        if (_batchRefImages.Count == 0)
        {
            RefImageInfo = "Chưa chọn ảnh nhân vật tham chiếu.";
            RefImageTag = "@character";
            HasRefImage = false;
            RefPreviewImagePath = string.Empty;
        }
        else
        {
            RefImageInfo = $"Đã tải {_batchRefImages.Count} ảnh: {Path.GetFileName(_batchRefImages[0].filePath)}";
            RefImageTag = string.Join(", ", _batchRefImages.Select(r => r.tag));
            HasRefImage = true;
            RefPreviewImagePath = _batchRefImages[0].filePath;
        }

        OnPropertyChanged(nameof(EmptyRefImageVisibility));
        OnPropertyChanged(nameof(HasRefImageVisibility));
    }

    public bool UpdateScriptJson(bool showInfoMessage = false)
    {
        if (string.IsNullOrWhiteSpace(ScriptJson)) return false;

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var parsed = JsonSerializer.Deserialize<ScenesJsonRootModel>(ScriptJson, options);
            if (parsed != null && parsed.scenes != null && parsed.scenes.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(parsed.video_title))
                {
                    ProjectTitle = parsed.video_title;
                }

                BatchImageItems.Clear();
                int idx = 1;
                foreach (var s in parsed.scenes)
                {
                    if (string.IsNullOrWhiteSpace(s.image_prompt)) continue;

                    var item = new BatchImageItem
                    {
                        Index = s.scene > 0 ? s.scene : idx,
                        // Use the same compact "001" format as the pipeline so newly
                        // imported scenes look identical to scenes already in the batch.
                        SceneTitle = BatchImageItem.NormalizeSceneTitle(
                            null,
                            s.scene > 0 ? s.scene : idx),
                        Transcript = s.transcript ?? string.Empty,
                        Prompt = s.image_prompt,
                        Status = "Waiting",
                        Engine = SelectedEngine,
                        Model = SelectedModel,
                        AspectRatio = SelectedAspect,
                        Upscale = SelectedUpscale
                    };
                    BatchImageItems.Add(item);
                    idx++;
                }

                AutoDetectAndMatchExistingImages(OutputDir);
                UpdateProgressUI();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateScriptJson] Error parsing JSON: {ex.Message}");
        }

        return false;
    }

    public void AutoDetectAndMatchExistingImages(string outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir)) return;

        try
        {
            var files = Directory.GetFiles(outputDir, "*.png")
                .Concat(Directory.GetFiles(outputDir, "*.jpg"))
                .Concat(Directory.GetFiles(outputDir, "*.webp"))
                .ToList();

            foreach (var item in BatchImageItems)
            {
                if (!string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath))
                {
                    item.Status = "Done";
                    continue;
                }

                item.ImagePath = string.Empty;

                if (files.Count > 0)
                {
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
                        continue;
                    }
                }

                item.Status = "Waiting";
            }

            UpdateProgressUI();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AutoDetectImages] Error matching files: {ex.Message}");
        }
    }

    public void UpdateProgressUI()
    {
        TotalCount = BatchImageItems.Count;
        TotalDoneCount = BatchImageItems.Count(i => i.IsDone || string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase));
        ProgressPercent = TotalCount > 0 ? (int)((TotalDoneCount * 100) / TotalCount) : 0;
        ProgressText = $"Đã tạo {TotalDoneCount}/{TotalCount} ảnh ({ProgressPercent}%)";
        // Refresh the watermark-removal button enable state whenever a batch
        // completes (gating whether there's any image to clean).
        HasDoneItems = BatchImageItems.Any(i =>
            string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(i.ImagePath)
            && File.Exists(i.ImagePath));
    }

    /// <summary>
    /// Re-enumerates all <c>Done</c> items and re-enqueues them for watermark
    /// removal. Backs the <c>🪄 Xóa Watermark</c> button in the editor header.
    ///
    /// Behavior:
    ///   - Pre-checks the CLI back-end via <see cref="IWatermarkRemover.ProbeAsync"/>.
    ///     If Node.js isn't installed or the CLI can't be reached, surfaces a
    ///     friendly "Cài Node.js" notification and aborts without touching items.
    ///   - Marks each item's <c>WatermarkRemoved = false</c> + clears
    ///     <c>WatermarkNote</c> so the badge goes back to "💧" while the CLI runs.
    ///   - Updates ProgressText in real time so the user sees how many images
    ///     have been processed out of the total.
    ///   - Waits for the pool to drain (up to 5 min) so the user gets a final
    ///     "Done" notification when the batch finishes.
    /// </summary>
    [RelayCommand]
    public async Task RemoveWatermarkAsync()
    {
        if (_watermarkQueue == null)
        {
            return;
        }

        var eligible = BatchImageItems
            .Where(i => string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(i.ImagePath)
                        && File.Exists(i.ImagePath))
            .ToList();

        if (eligible.Count == 0)
        {
            return;
        }

        // Pre-flight: probe the CLI back-end so the user doesn't wait 5 min only
        // to discover Node.js was missing. We surface a short Vietnamese
        // message and let the Settings page handle the actual install.
        if (_watermarkRemover != null)
        {
            try
            {
                var status = await _watermarkRemover.ProbeAsync();
                if (!status.IsAvailable)
                {
                    StatusMessage = $"Watermark CLI chưa sẵn sàng: {status.Diagnostic}. Mở Settings → Gemini Watermark Removal → bấm 'Kiểm tra Node.js & CLI'.";
                    return;
                }
            }
            catch (Exception probeEx)
            {
                StatusMessage = $"Không probe được Watermark CLI: {probeEx.Message}";
                return;
            }
        }

        // Master toggle: user disabled watermark removal in Settings.
        if (_configService != null && !_configService.CurrentSettings.EnableWatermarkRemoval)
        {
            StatusMessage = "Watermark removal đang TẮT trong Settings. Bật rồi thử lại.";
            return;
        }

        IsGenerating = true;
        int total = eligible.Count;
        int done = 0;
        ProgressText = $"Đang xóa watermark 0/{total} ảnh...";

        // Reset badges to in-progress state so the UI shows the work is happening.
        var uiSyncContext = SynchronizationContext.Current;
        foreach (var item in eligible)
        {
            void Apply()
            {
                item.WatermarkRemoved = false;
                item.WatermarkNote = "Đang xóa watermark...";
            }
            if (uiSyncContext != null)
            {
                uiSyncContext.Post(_ => Apply(), null);
            }
            else
            {
                Apply();
            }
        }

        // Track completion so we can update ProgressText in real time without
        // having to read from BatchImageItems (which the pool mutates from a
        // background thread).
        foreach (var item in eligible)
        {
            var captured = item;
            captured.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(BatchImageItem.WatermarkRemoved) && captured.WatermarkRemoved)
                {
                    int current = System.Threading.Interlocked.Increment(ref done);
                    int percent = (current * 100) / total;
                    void ApplyProgress()
                    {
                        ProgressText = $"Đã xóa watermark {current}/{total} ảnh ({percent}%).";
                    }
                    if (uiSyncContext != null)
                    {
                        uiSyncContext.Post(_ => ApplyProgress(), null);
                    }
                    else
                    {
                        ApplyProgress();
                    }
                }
            };
        }

        // Enqueue to the parallel pool. The pool will fire-and-forget; we wait
        // for completion below so the SaveCurrentProjectStateAsync at the end
        // captures the final states.
        foreach (var item in eligible)
        {
            _watermarkQueue.Enqueue(item, item.ImagePath, uiSyncContext);
        }

        // Wait up to 5 minutes for the pool to drain.
        await _watermarkQueue.WaitForCompletionAsync(TimeSpan.FromMinutes(5));

        // Persist project state so the new WatermarkRemoved = true survives
        // app restarts.
        if (ActiveProject != null)
        {
            await SaveCurrentProjectStateAsync();
        }

        int cleaned = BatchImageItems.Count(i => i.WatermarkRemoved);
        UpdateProgressUI();
        IsGenerating = false;
        StatusMessage = cleaned > 0
            ? $"Hoàn tất: đã xóa watermark cho {cleaned}/{total} ảnh."
            : $"Không có ảnh nào được xóa watermark (kiểm tra log để biết lý do).";
    }

    /// <summary>
    /// Refreshes the <see cref="HasDoneItems"/> boolean. Called automatically
    /// from <see cref="UpdateProgressUI"/>; callers can also invoke after batch
    /// edges that mutate <see cref="BatchImageItems"/>.
    /// </summary>
    public void RefreshHasDoneItems()
    {
        HasDoneItems = BatchImageItems.Any(i =>
            string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(i.ImagePath)
            && File.Exists(i.ImagePath));
    }

    public async Task OnNewProjectCreatedAsync(string projectName)
    {
        if (_batchProjectService == null) return;

        var newProj = await _batchProjectService.CreateProjectAsync(projectName, ScriptJson);
        ActiveProject = newProj;

        await OpenProjectAsync(newProj);
        await LoadProjectsListAsync();
    }

    public async Task GenerateBatchImagesAsync(
        Func<string, string, string, Task<bool>> confirmWarning,
        Action<string, string> showNotification)
    {
        if (IsGenerating) return;

        if (BatchImageItems.Count == 0)
        {
            UpdateScriptJson(showInfoMessage: false);
        }

        if (BatchImageItems.Count == 0)
        {
            showNotification("Thông báo", "Chưa có cảnh nào để tạo! Vui lòng nhập JSON kịch bản và bấm 'Cập nhật kịch bản'.");
            return;
        }

        if (_batchRefImages.Count == 0)
        {
            bool proceed = await confirmWarning(
                "Thiếu ảnh nhân vật gốc",
                "Chú ý: Bạn chưa chọn ảnh nhân vật tham chiếu.\nCác prompt chứa tag '@character' sẽ được sinh ảnh không có nhân vật gốc.\n\nBạn có muốn tiếp tục sinh ảnh hàng loạt không?",
                "Tiếp tục"
            );

            if (!proceed) return;
        }

        // ── History hook: ghi entry Running ──
        string? historyId = null;
        if (_historyService != null)
        {
            try
            {
                historyId = await _historyService.StartTaskRunAsync(new AssetAutomator.Core.Models.TaskRunHistoryEntry
                {
                    TaskType = AssetAutomator.Core.Models.HistoryTaskType.BatchImageGen,
                    ProjectName = !string.IsNullOrWhiteSpace(ActiveProject?.ProjectName) ? ActiveProject.ProjectName : ProjectTitle,
                    FlowProjectId = ActiveProject?.FlowProjectId,
                    OutputDirectory = OutputDir,
                    Status = AssetAutomator.Core.Models.HistoryTaskStatus.Running,
                    StartedAt = DateTime.Now,
                    LogsSummary = $"Bắt đầu sinh ảnh (concurrency={SelectedConcurrency}).",
                });
            }
            catch (Exception histEx)
            {
                System.Diagnostics.Debug.WriteLine($"[BatchHistory-Start] {histEx.Message}");
            }
        }

        IsGenerating = true;

        // Google Flow Local API is the only image-gen backend now.
        string provider = ImageGenProviderFactory.FlowLocalProviderKey;
        string serverUrl = _configService?.CurrentSettings.ImageApiUrl ?? "http://127.0.0.1:8787/v1";
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            serverUrl = "http://127.0.0.1:8787/v1";
        }
        string apiKey = _configService?.CurrentSettings.ImageApiKey ?? "flow-local-key";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = "flow-local-key";
        }

        string engine = SelectedEngine;
        string model = SelectedModel;
        string aspectRatio = SelectedAspect;
        string upscale = SelectedUpscale;
        int maxConcurrency = Math.Max(1, SelectedConcurrency);

        string outputDir = OutputDir;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");
            OutputDir = outputDir;
        }
        Directory.CreateDirectory(outputDir);

        AutoDetectAndMatchExistingImages(outputDir);

        var itemsToGenerate = BatchImageItems
            .Where(i => i.Status != "Done" || string.IsNullOrEmpty(i.ImagePath) || !File.Exists(i.ImagePath))
            .ToList();

        if (itemsToGenerate.Count == 0)
        {
            IsGenerating = false;
            showNotification("Thông báo", "Tất cả các cảnh trong dự án đã có ảnh hợp lệ trên đĩa!");
            return;
        }

        // Single backend: Google Flow Local.
        string projectTitle = !string.IsNullOrWhiteSpace(ActiveProject?.ProjectName)
            ? ActiveProject.ProjectName
            : (!string.IsNullOrWhiteSpace(ProjectTitle) ? ProjectTitle : "Batch Project");

        string? flowProjId = ActiveProject?.FlowProjectId;
        if (string.IsNullOrEmpty(flowProjId))
        {
            var (pId, pUrl, _) = await FlowLocalImageGenProvider.CreateProjectAsync(serverUrl, apiKey, projectTitle);
            if (!string.IsNullOrEmpty(pId))
            {
                flowProjId = pId;
                if (ActiveProject != null)
                {
                    ActiveProject.FlowProjectId = pId;
                    ActiveProject.FlowProjectUrl = pUrl;
                    await SaveCurrentProjectStateAsync();
                }
            }
        }

        foreach (var item in itemsToGenerate)
        {
            item.Provider = provider;
            item.Engine = engine;
            item.Model = model;
            item.AspectRatio = aspectRatio;
            item.Upscale = upscale;
            item.Status = "Waiting";
            item.ErrorMessage = string.Empty;
            item.ImagePath = string.Empty;
            item.FlowProjectId = flowProjId;
            item.FlowProjectTitle = projectTitle;
        }

        FlowLocalImageGenProvider.ClearReferenceMediaCache();

        // Run batch inline on UI thread: HttpClient.SendAsync is async I/O and
        // does not block the dispatcher. Running inside Task.Run previously caused
        // RPC_E_WRONG_THREAD crashes when the provider set ObservableCollection
        // item properties from a thread-pool thread.
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = itemsToGenerate.Select(async item =>
        {
            await semaphore.WaitAsync();
            try
            {
                try
                {
                    if (_batchImageGenService != null)
                    {
                        await _batchImageGenService.ProcessSingleImageItemAsync(
                            item,
                            serverUrl,
                            apiKey,
                            _batchRefImages,
                            outputDir
                        );
                    }
                }
                catch (Exception itemEx)
                {
                    item.Status = "Failed";
                    item.ErrorMessage = $"Error: {itemEx.Message}";
                    item.FinishedAt = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine($"[Batch] item {item.Index} failed: {itemEx.Message}");
                }

                UpdateProgressUI();
                if (ActiveProject != null)
                {
                    await SaveCurrentProjectStateAsync();
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        UpdateProgressUI();
        if (ActiveProject != null)
        {
            await SaveCurrentProjectStateAsync();
        }

        IsGenerating = false;
        showNotification("Hoàn thành", $"Đã hoàn tất sinh {itemsToGenerate.Count} ảnh cảnh hàng loạt!");

        // ── History hook: mark Finished + scan output dir cho assets ──
        if (_historyService != null && !string.IsNullOrWhiteSpace(historyId))
        {
            try
            {
                int doneCount = BatchImageItems.Count(i => string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase));
                int failedCount = BatchImageItems.Count(i => string.Equals(i.Status, "Failed", StringComparison.OrdinalIgnoreCase));
                var status = failedCount > 0
                    ? (doneCount > 0 ? AssetAutomator.Core.Models.HistoryTaskStatus.Success : AssetAutomator.Core.Models.HistoryTaskStatus.Failed)
                    : AssetAutomator.Core.Models.HistoryTaskStatus.Success;
                string summary = $"Batch image gen: {doneCount} done / {failedCount} failed.";
                await _historyService.FinishTaskRunAsync(historyId, status, null, summary);

                if (Directory.Exists(outputDir))
                {
                    var pngs = Directory.GetFiles(outputDir, "*.png");
                    if (pngs.Length > 0)
                    {
                        await _historyService.AppendAssetPathsAsync(historyId, pngs);
                    }
                }
            }
            catch (Exception histEx)
            {
                System.Diagnostics.Debug.WriteLine($"[BatchHistory-Finish] {histEx.Message}");
            }
        }
    }

    /// <summary>
    /// Locates the on-disk scenes.json file for the active project.
    ///
    /// Pipeline-managed projects store <c>project.json.ScriptJson</c> as an
    /// absolute path to scenes.json (see <c>SceneImageBatchStep</c>); user-created
    /// projects store the actual JSON content. So we have to try in priority
    /// order:
    ///   1. <c>ScriptJson</c> itself if it points at an existing .json file.
    /// Returns the parent directory of <paramref name="outputDir"/>, but only
    /// when that parent is a real folder on disk (so we never feed
    /// <c>Path.GetDirectoryName(null!)</c> to <see cref="File.Exists"/>).
    /// </summary>
    private static string? TryGetParentOfImagesDir(string? outputDir)
    {
        if (string.IsNullOrWhiteSpace(outputDir)) return null;
        try
        {
            string? parent = Directory.GetParent(outputDir)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }
        catch
        {
            // Invalid path (e.g. contains illegal chars). Just return null.
        }
        return null;
    }

    private static string GetDefaultScriptJson()
    {
        return @"{
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
}