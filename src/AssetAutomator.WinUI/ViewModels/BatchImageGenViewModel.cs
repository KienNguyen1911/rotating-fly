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

    // Config & Engine properties
    [ObservableProperty]
    private string _selectedAspect = "16:9";

    [ObservableProperty]
    private string _selectedEngine = "flow";

    [ObservableProperty]
    private string _selectedProvider = "glabs";

    [ObservableProperty]
    private string _selectedModel = "nano_banana_2";

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

    [ObservableProperty]
    private bool _isGenerating = false;

    public string GenerateButtonText => $"⚡ Tạo hàng loạt ({SelectedConcurrency} ảnh song song)";

    public Microsoft.UI.Xaml.Visibility GlabsModelsVisibility => SelectedProvider == "glabs" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FlowLocalModelsVisibility => SelectedProvider == "flow_local" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility FlowOptionsVisibility => SelectedEngine == "flow" ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    partial void OnSelectedProviderChanged(string value)
    {
        OnPropertyChanged(nameof(GlabsModelsVisibility));
        OnPropertyChanged(nameof(FlowLocalModelsVisibility));
        if (value == "flow_local" && SelectedModel.StartsWith("nano_banana"))
        {
            SelectedModel = "gemini-3.1-flash-image";
        }
        else if (value == "glabs" && !SelectedModel.StartsWith("nano_banana"))
        {
            SelectedModel = "nano_banana_2";
        }
    }

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
        IConfigService? configService = null)
    {
        _batchProjectService = batchProjectService;
        _batchImageGenService = batchImageGenService;
        _configService = configService;

        ScriptJson = GetDefaultScriptJson();
        OutputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "BatchImages");

        _ = LoadProjectsListAsync();
    }

    [RelayCommand]
    public async Task LoadProjectsListAsync()
    {
        if (_batchProjectService != null)
        {
            ProjectsStoragePath = _batchProjectService.GetProjectsBaseDirectory();
            var list = await _batchProjectService.GetAllProjectsAsync();
            Projects.Clear();
            foreach (var proj in list)
            {
                Projects.Add(proj);
            }
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
        SelectedProvider = string.IsNullOrWhiteSpace(project.Provider) ? "glabs" : project.Provider;
        SelectedModel = string.IsNullOrWhiteSpace(project.Model) ? "nano_banana_2" : project.Model;
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
                    SceneTitle = state.SceneTitle,
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
                    Upscale = state.Upscale
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
        ActiveProject.Provider = SelectedProvider;
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
            Upscale = item.Upscale
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
                        SceneTitle = $"Cảnh {(s.scene > 0 ? s.scene : idx)}",
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

        IsGenerating = true;

        string provider = SelectedProvider;
        string serverUrl;
        string apiKey;

        if (provider == "flow_local")
        {
            serverUrl = "http://127.0.0.1:8787/v1";
            apiKey = "flow-local-key";
        }
        else
        {
            serverUrl = _configService?.CurrentSettings.ImageApiUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(serverUrl)) serverUrl = "http://127.0.0.1:8765";
            apiKey = _configService?.CurrentSettings.ImageApiKey ?? string.Empty;
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

        if (provider == "flow_local")
        {
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
        }
        else
        {
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
            }
        }

        FlowLocalImageGenProvider.ClearReferenceMediaCache();

        await Task.Run(async () =>
        {
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = itemsToGenerate.Select(async item =>
            {
                await semaphore.WaitAsync();
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
        });

        UpdateProgressUI();
        if (ActiveProject != null)
        {
            await SaveCurrentProjectStateAsync();
        }

        IsGenerating = false;
        showNotification("Hoàn thành", $"Đã hoàn tất sinh {itemsToGenerate.Count} ảnh cảnh hàng loạt!");
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
