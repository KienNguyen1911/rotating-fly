using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Application.Steps;
using AssetAutomator.Core.Constants;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Pipeline Orchestrator — ma trận hàng ngang (tasks) × hàng dọc (stages) với giới hạn slot riêng từng stage.
    ///
    /// Mô hình Assembly Line:
    ///   - Mỗi task chạy độc lập qua 6 stage tuần tự.
    ///   - Mỗi stage có SemaphoreSlim riêng giới hạn số task đồng thời.
    ///   - Khi 1 task hoàn thành stage A, nó lập tức vào stage B (nếu còn slot), nhường slot A cho task khác.
    ///
    /// Slot mặc định:
    ///   Deep Research       : 2
    ///   Voiceover           : 3
    ///   Scene Segmentation  : 4 (STAGE C - NEW)
    ///   Image Prompt Gen    : 4 (STAGE D - NEW)
    ///   Image Gen           : 1 (tuần tự tuyệt đối, sleep 15s giữa các task)
    /// </summary>
    public class PipelineOrchestrator
    {
        private readonly GeminiApiService _geminiApiService;
        private readonly IConfigService _configService;
        private readonly VoiceoverGenerationStep _voiceoverStep;
        private readonly SceneSegmentationStep _sceneSegmentationStep;
        private readonly ImagePromptGenerationStep _imagePromptStep;
        private readonly BatchImageGenService _batchImageGenService;
        private readonly GeminiTopicResearchStep _topicResearchStep;
        private readonly SceneImageBatchStep _imageBatchStep;
        private readonly HistoryService? _historyService;

        private readonly int _maxDeepResearch;
        private readonly int _maxVoiceover;
        private readonly int _maxSceneSegmentation;
        private readonly int _maxImagePromptGen;
        private readonly int _maxImageGen;
        private readonly int _maxGeminiConcurrency;

        public PipelineOrchestrator(
            GeminiApiService geminiApiService,
            IConfigService configService,
            VoiceoverGenerationStep voiceoverStep,
            SceneSegmentationStep sceneSegmentationStep,
            ImagePromptGenerationStep imagePromptStep,
            BatchImageGenService batchImageGenService,
            GeminiTopicResearchStep topicResearchStep,
            SceneImageBatchStep imageBatchStep,
            HistoryService? historyService = null,
            int maxDeepResearch = 1,
            int maxVoiceover = 1,
            int maxSceneSegmentation = 1,
            int maxImagePromptGen = 1,
            int maxImageGen = 1,
            int maxGeminiConcurrency = 2)
        {
            _geminiApiService = geminiApiService;
            _configService = configService;
            _voiceoverStep = voiceoverStep;
            _sceneSegmentationStep = sceneSegmentationStep;
            _imagePromptStep = imagePromptStep;
            _batchImageGenService = batchImageGenService;
            _topicResearchStep = topicResearchStep;
            _imageBatchStep = imageBatchStep;
            _historyService = historyService;
            _maxDeepResearch = Math.Max(1, maxDeepResearch);
            _maxVoiceover = Math.Max(1, maxVoiceover);
            _maxSceneSegmentation = Math.Max(1, maxSceneSegmentation);
            _maxImagePromptGen = Math.Max(1, maxImagePromptGen);
            _maxImageGen = Math.Max(1, maxImageGen);
            _maxGeminiConcurrency = Math.Max(1, maxGeminiConcurrency);
        }

        /// <summary>
        /// Thực thi batch Gemini tasks với ma trận pipeline.
        /// Tất cả task khởi động cùng lúc, mỗi task tự đi qua 6 stage với semaphore riêng.
        /// Callback logTask được gọi từ thread pool — caller phải tự Dispatch nếu cần cập nhật UI.
        /// </summary>
        public async Task<PipelineBatchResult> ExecuteBatchAsync(
            List<GeminiTaskModel> taskModels,
            Action<GeminiTaskModel, string> logTask,
            CancellationToken cancellationToken = default)
        {
            var result = new PipelineBatchResult();
            var startedAt = DateTime.Now;

            using var deepResearchSem = new SemaphoreSlim(_maxDeepResearch, _maxDeepResearch);
            using var voiceoverSem = new SemaphoreSlim(_maxVoiceover, _maxVoiceover);
            using var sceneSegSem = new SemaphoreSlim(_maxSceneSegmentation, _maxSceneSegmentation);
            using var imagePromptSem = new SemaphoreSlim(_maxImagePromptGen, _maxImagePromptGen);
            using var imageGenSem = new SemaphoreSlim(_maxImageGen, _maxImageGen);
            // Shared Gemini gate — caps the number of in-flight calls to any Gemini
            // endpoint (Deep Research / Scene Seg / Image Prompt). Capacity is
            // intentionally shared across stages so 3+ tasks still finish without
            // tripping 429 even though per-stage concurrency may be higher.
            using var geminiConcurrencySem = new SemaphoreSlim(_maxGeminiConcurrency, _maxGeminiConcurrency);

            // ── Ma trận: mỗi task là 1 hàng, chạy song song ──
            var taskRunners = taskModels.Select(taskModel =>
                ProcessOneTaskThroughPipelineAsync(
                    taskModel,
                    deepResearchSem,
                    voiceoverSem,
                    sceneSegSem,
                    imagePromptSem,
                    imageGenSem,
                    geminiConcurrencySem,
                    logTask,
                    cancellationToken
                ));

            await Task.WhenAll(taskRunners);

            result.TotalTasks = taskModels.Count;
            result.SuccessCount = taskModels.Count(t => t.Status == NodeStatus.Success);
            result.FailedCount = taskModels.Count(t => t.Status == NodeStatus.Failed);
            result.Elapsed = DateTime.Now - startedAt;
            return result;
        }

        /// <summary>
        /// Xử lý MỘT task qua toàn bộ pipeline 6 stage với slot giới hạn từng stage.
        /// </summary>
        private async Task ProcessOneTaskThroughPipelineAsync(
            GeminiTaskModel taskModel,
            SemaphoreSlim deepResearchSem,
            SemaphoreSlim voiceoverSem,
            SemaphoreSlim sceneSegSem,
            SemaphoreSlim imagePromptSem,
            SemaphoreSlim imageGenSem,
            SemaphoreSlim geminiConcurrencySem,
            Action<GeminiTaskModel, string> logTask,
            CancellationToken ct)
        {
            string topic = taskModel.Topic.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                taskModel.Status = NodeStatus.Failed;
                taskModel.CurrentStepInfo = "Lỗi: Chưa nhập topic";
                logTask(taskModel, $"[PIPELINE-ERROR] Task bị bỏ qua vì chưa có topic.");
                return;
            }

            // ── Build internal AutomationTask ──
            var internalTask = BuildInternalAutomationTask(taskModel);

            // ── History hook: ghi entry Running vào SQLite (best-effort, không fail pipeline nếu lỗi) ──
            string? historyId = null;
            string projectName = !string.IsNullOrWhiteSpace(taskModel.OutputFolderName)
                ? taskModel.OutputFolderName
                : Core.Constants.YoutubeHelper.ToSafeTopicSlug(topic);
            string? outputDir = !string.IsNullOrWhiteSpace(internalTask.OutputFolderOverride)
                ? ResolveOutputDir(internalTask.OutputFolderOverride)
                : null;
            if (_historyService != null)
            {
                try
                {
                    historyId = await _historyService.StartTaskRunAsync(new Core.Models.TaskRunHistoryEntry
                    {
                        TaskType = Core.Models.HistoryTaskType.FullPipeline,
                        ProjectName = projectName,
                        OutputDirectory = outputDir,
                        Status = Core.Models.HistoryTaskStatus.Running,
                        StartedAt = DateTime.Now,
                        LogsSummary = $"Pipeline bắt đầu: {topic}",
                        ScriptwriterGemName = taskModel.SelectedScriptwriterGem?.Name,
                        SceneCreatorGemName = taskModel.SelectedSceneCreatorGem?.Name,
                    });
                }
                catch (Exception histEx)
                {
                    logTask(taskModel, $"[HISTORY-WARN] Không ghi được history start: {histEx.Message}");
                }
            }

            taskModel.Status = NodeStatus.Running;
            logTask(taskModel, $"[PIPELINE] 🚀 Task '{topic}' vào hàng đợi pipeline...");

            // Forward log từ internal AutomationTask sang GeminiTaskModel
            Action<AutomationTask, string> internalLog = (t, msg) => logTask(taskModel, msg);

            try
            {
                // ═══════════════════════════════════════════════
                // STAGE A: Deep Research & Transcript (max 2)
                // ═══════════════════════════════════════════════
                await deepResearchSem.WaitAsync(ct);
                try
                {
                    int used = _maxDeepResearch - deepResearchSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-A] 🔍 Bắt đầu Deep Research... (slot {used}/{_maxDeepResearch})");
                    taskModel.Step1Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage A: Deep Research ({used}/{_maxDeepResearch})";

                    // Shared Gemini gate — chỉ chờ slot ở bước này nếu stage thực sự
                    // sẽ gọi Gemini API (skip-check nằm trong RunStageDeepResearchAsync,
                    // nên task có transcript.txt sẵn sẽ thoát nhanh không chiếm slot).
                    int gemUsed = _maxGeminiConcurrency - geminiConcurrencySem.CurrentCount;
                    if (gemUsed >= _maxGeminiConcurrency)
                    {
                        logTask(taskModel, $"[GEMINI-GATE] 🎫 Stage A đang chờ slot Gemini ({gemUsed}/{_maxGeminiConcurrency})...");
                    }
                    await geminiConcurrencySem.WaitAsync(ct);
                    try
                    {
                        await RunStageDeepResearchAsync(internalTask, taskModel, internalLog);
                    }
                    finally
                    {
                        geminiConcurrencySem.Release();
                    }
                }
                catch (Exception ex)
                {
                    taskModel.Step1Status = NodeStatus.Failed;
                    taskModel.CurrentStepInfo = $"❌ Stage A Lỗi: {ex.Message}";
                    logTask(taskModel, $"[ERROR] [STAGE-A] Deep Research thất bại: {ex.Message}");
                    throw;
                }
                finally
                {
                    deepResearchSem.Release();
                    if (taskModel.Step1Status == NodeStatus.Running)
                    {
                        taskModel.Step1Status = NodeStatus.Success;
                        logTask(taskModel, $"[STAGE-A] ✅ Deep Research hoàn thành.");
                    }
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE B: Voiceover & SRT (max 3)
                // ═══════════════════════════════════════════════
                await voiceoverSem.WaitAsync(ct);
                try
                {
                    int used = _maxVoiceover - voiceoverSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-B] 🎙️ Bắt đầu Voiceover AI84... (slot {used}/{_maxVoiceover})");
                    taskModel.Step2Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage B: Voiceover AI84 ({used}/{_maxVoiceover})";

                    await RunStageVoiceoverAsync(internalTask, taskModel, internalLog);
                }
                catch (Exception ex)
                {
                    taskModel.Step2Status = NodeStatus.Failed;
                    taskModel.CurrentStepInfo = $"❌ Stage B Lỗi: {ex.Message}";
                    logTask(taskModel, $"[ERROR] [STAGE-B] Voiceover thất bại: {ex.Message}");
                    throw;
                }
                finally
                {
                    voiceoverSem.Release();
                    if (taskModel.Step2Status == NodeStatus.Running)
                    {
                        taskModel.Step2Status = NodeStatus.Success;
                        logTask(taskModel, $"[STAGE-B] ✅ Voiceover hoàn thành.");
                    }
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE C: Scene Segmentation (max 4)
                // ═══════════════════════════════════════════════
                await sceneSegSem.WaitAsync(ct);
                try
                {
                    int used = _maxSceneSegmentation - sceneSegSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-C] 🎬 Bắt đầu Scene Segmentation... (slot {used}/{_maxSceneSegmentation})");
                    taskModel.Step3Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage C: Scene Segmentation ({used}/{_maxSceneSegmentation})";

                    int gemUsed = _maxGeminiConcurrency - geminiConcurrencySem.CurrentCount;
                    if (gemUsed >= _maxGeminiConcurrency)
                    {
                        logTask(taskModel, $"[GEMINI-GATE] 🎫 Stage C đang chờ slot Gemini ({gemUsed}/{_maxGeminiConcurrency})...");
                    }
                    await geminiConcurrencySem.WaitAsync(ct);
                    try
                    {
                        await RunStageSceneSegmentationAsync(internalTask, taskModel, internalLog);
                    }
                    finally
                    {
                        geminiConcurrencySem.Release();
                    }
                }
                catch (Exception ex)
                {
                    // Set Step3Status to Failed immediately when error occurs
                    taskModel.Step3Status = NodeStatus.Failed;
                    taskModel.CurrentStepInfo = $"❌ Stage C Lỗi: {ex.Message}";
                    logTask(taskModel, $"[ERROR] [STAGE-C] Scene Segmentation thất bại: {ex.Message}");
                    throw; // Re-throw to let outer handler deal with it
                }
                finally
                {
                    sceneSegSem.Release();
                    // Only mark Success if it wasn't already set to Failed by catch
                    if (taskModel.Step3Status == NodeStatus.Running)
                    {
                        taskModel.Step3Status = NodeStatus.Success;
                        logTask(taskModel, $"[STAGE-C] ✅ Scene Segmentation hoàn thành.");
                    }
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE D: Image Prompt Generation (max 4)
                // ═══════════════════════════════════════════════
                await imagePromptSem.WaitAsync(ct);
                try
                {
                    int used = _maxImagePromptGen - imagePromptSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-D] 🎨 Bắt đầu Image Prompt Generation... (slot {used}/{_maxImagePromptGen})");
                    taskModel.Step4Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage D: Image Prompt ({used}/{_maxImagePromptGen})";

                    int gemUsed = _maxGeminiConcurrency - geminiConcurrencySem.CurrentCount;
                    if (gemUsed >= _maxGeminiConcurrency)
                    {
                        logTask(taskModel, $"[GEMINI-GATE] 🎫 Stage D đang chờ slot Gemini ({gemUsed}/{_maxGeminiConcurrency})...");
                    }
                    await geminiConcurrencySem.WaitAsync(ct);
                    try
                    {
                        await RunStageImagePromptGenAsync(internalTask, taskModel, internalLog);
                    }
                    finally
                    {
                        geminiConcurrencySem.Release();
                    }
                }
                catch (Exception ex)
                {
                    taskModel.Step4Status = NodeStatus.Failed;
                    taskModel.CurrentStepInfo = $"❌ Stage D Lỗi: {ex.Message}";
                    logTask(taskModel, $"[ERROR] [STAGE-D] Image Prompt Generation thất bại: {ex.Message}");
                    throw;
                }
                finally
                {
                    imagePromptSem.Release();
                    if (taskModel.Step4Status == NodeStatus.Running)
                    {
                        taskModel.Step4Status = NodeStatus.Success;
                        logTask(taskModel, $"[STAGE-D] ✅ Image Prompt Generation hoàn thành.");
                    }
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE E: Image Generation (max 1 — tuần tự)
                // ═══════════════════════════════════════════════
                await imageGenSem.WaitAsync(ct);
                try
                {
                    logTask(taskModel, $"[STAGE-E] 🖼️ Bắt đầu Image Generation... (1/1 slot)");
                    taskModel.Step5Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = "Stage E: Image Gen (1/1)";

                    await RunStageImageGenAsync(internalTask, taskModel, internalLog);
                }
                catch (Exception ex)
                {
                    taskModel.Step5Status = NodeStatus.Failed;
                    taskModel.CurrentStepInfo = $"❌ Stage E Lỗi: {ex.Message}";
                    logTask(taskModel, $"[ERROR] [STAGE-E] Image Generation thất bại: {ex.Message}");
                    throw;
                }
                finally
                {
                    imageGenSem.Release();
                    if (taskModel.Step5Status == NodeStatus.Running)
                    {
                        taskModel.Step5Status = NodeStatus.Success;
                        logTask(taskModel, $"[STAGE-E] ✅ Image Generation hoàn thành.");
                    }
                }

                // ── Hoàn thành ──
                taskModel.Status = NodeStatus.Success;
                taskModel.CurrentStepInfo = "✔️ Hoàn thành 100%";
                logTask(taskModel, $"[PIPELINE] 🎉 Task '{topic}' hoàn thành toàn bộ pipeline!");

                // ── History hook: mark Success + scan output dir cho assets ──
                await FinishHistoryAsync(historyId, Core.Models.HistoryTaskStatus.Success, null, $"Pipeline hoàn thành: {topic}", outputDir);
            }
            catch (OperationCanceledException)
            {
                taskModel.Status = NodeStatus.Failed;
                taskModel.CurrentStepInfo = "⏹️ Đã hủy";
                logTask(taskModel, $"[PIPELINE] ⏹️ Task '{topic}' bị hủy.");
                await FinishHistoryAsync(historyId, Core.Models.HistoryTaskStatus.Cancelled, null, $"Pipeline bị hủy: {topic}", outputDir);
            }
            catch (Exception ex)
            {
                taskModel.Status = NodeStatus.Failed;
                taskModel.CurrentStepInfo = $"❌ Lỗi: {ex.Message}";
                logTask(taskModel, $"[PIPELINE-ERROR] ❌ Task '{topic}' thất bại: {ex.Message}");
                await FinishHistoryAsync(historyId, Core.Models.HistoryTaskStatus.Failed, ex.Message, $"Pipeline lỗi: {ex.Message}", outputDir);
            }
        }

        /// <summary>
        /// Helper: ghi history finished (status + assets scan). Best-effort — không
        /// bao giờ throw ra ngoài để tránh nuốt exception pipeline thật.
        /// </summary>
        private async Task FinishHistoryAsync(string? historyId, Core.Models.HistoryTaskStatus status, string? error, string? summary, string? outputDir)
        {
            if (_historyService == null || string.IsNullOrWhiteSpace(historyId)) return;
            try
            {
                await _historyService.FinishTaskRunAsync(historyId, status, error, summary);
                // Scan output dir cho ảnh scenes — append vào asset paths.
                if (!string.IsNullOrWhiteSpace(outputDir) && Directory.Exists(outputDir))
                {
                    try
                    {
                        var assetFiles = new List<string>();
                        string imgDir = Path.Combine(outputDir, "img");
                        if (Directory.Exists(imgDir))
                        {
                            assetFiles.AddRange(Directory.GetFiles(imgDir, "*.png"));
                            assetFiles.AddRange(Directory.GetFiles(imgDir, "*.jpg"));
                            assetFiles.AddRange(Directory.GetFiles(imgDir, "*.webp"));
                        }
                        // Voiceover outputs (mp3/wav/srt) cũng được coi là assets
                        foreach (var ext in new[] { "voiceover.mp3", "voiceover.wav", "voiceover.srt", "transcript.txt", "scenes.json" })
                        {
                            var p = Path.Combine(outputDir, ext);
                            if (File.Exists(p)) assetFiles.Add(p);
                        }
                        if (assetFiles.Count > 0)
                        {
                            await _historyService.AppendAssetPathsAsync(historyId, assetFiles);
                        }
                    }
                    catch
                    {
                        // Asset scan failure không critical.
                    }
                }
            }
            catch (Exception histEx)
            {
                System.Diagnostics.Debug.WriteLine($"[History-Finish] {histEx.Message}");
            }
        }

        /// <summary>
        /// Resolve absolute output directory dựa trên OutputsDir config (giống pattern trong BatchImageGenViewModel).
        /// </summary>
        private string ResolveOutputDir(string folderName)
        {
            string? baseDir = _configService?.CurrentSettings?.OutputsDir;
            if (string.IsNullOrWhiteSpace(baseDir))
            {
                baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Outputs");
            }
            return Path.Combine(baseDir, "Gemini", folderName);
        }

        // ─────────────────────────────────────────────────────
        //  Stage runners
        // ─────────────────────────────────────────────────────

        private async Task RunStageDeepResearchAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            Directory.CreateDirectory(outputDir);
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            // Strict primary-folder check only — do NOT scan sibling folders.
            // The skip must reflect real assets in the current task's folder so subsequent
            // stages (which read from the same path) can actually consume them.
            if (File.Exists(transcriptPath) && new FileInfo(transcriptPath).Length > 50)
            {
                log(task, $"[STAGE-A] ⏭️ transcript.txt đã tồn tại trong folder hiện tại ({new FileInfo(transcriptPath).Length} bytes), bỏ qua Deep Research.");
                task.Step2Status = "Done";
                return;
            }

            string gemId = taskModel.SelectedScriptwriterGem?.Id ?? string.Empty;
            string model = GeminiApiService.ResolveModelName(taskModel.ScriptwriterModel);
            bool deepResearch = taskModel.EnableDeepResearch;

            await _topicResearchStep.ExecuteAsync(
                topicOrUrl: taskModel.Topic.Trim(),
                outputDir: outputDir,
                gemId: string.IsNullOrWhiteSpace(gemId) ? null : gemId,
                enableDeepResearch: deepResearch,
                task: task,
                logTask: log,
                selectedModel: model,
                existingSessionId: null,
                scriptMinWords: taskModel.ScriptMinWords,
                scriptTargetWords: taskModel.ScriptTargetWords,
                scriptMaxWords: taskModel.ScriptMaxWords
            );
        }

        private async Task RunStageVoiceoverAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string mp3Path = Path.Combine(outputDir, "voiceover.mp3");
            string wavPath = Path.Combine(outputDir, "voiceover.wav");
            string srtPath = Path.Combine(outputDir, "voiceover.srt");

            // Strict primary-folder check only.
            bool hasAudio = (File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 1000)
                         || (File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000);
            bool hasSrt = File.Exists(srtPath) && new FileInfo(srtPath).Length > 10;

            if (hasAudio && hasSrt)
            {
                log(task, $"[STAGE-B] ⏭️ Voiceover/SRT đã tồn tại trong folder hiện tại, bỏ qua Voiceover.");
                task.Step4Status = "Done";
                task.StepSrtStatus = "Done";
                return;
            }

            string transcriptPath = Path.Combine(outputDir, "transcript.txt");
            string scriptText = File.Exists(transcriptPath)
                ? await File.ReadAllTextAsync(transcriptPath)
                : string.Empty;

            await _voiceoverStep.ExecuteAsync(
                task: task,
                logTask: log,
                apiKey: _configService.CurrentSettings.Ai84ApiKey,
                voiceId: taskModel.VoiceId.Trim(),
                outputDir: outputDir,
                scriptText: scriptText,
                videoId: task.VideoId
            );
        }

        private async Task RunStageSceneSegmentationAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesRawPath = Path.Combine(outputDir, "scenes_raw.json");

            // Check if scenes_raw.json already exists
            if (File.Exists(scenesRawPath) && new FileInfo(scenesRawPath).Length > 50 && IsValidScenesRawJson(scenesRawPath))
            {
                log(task, $"[STAGE-C] ⏭️ scenes_raw.json hợp lệ đã tồn tại trong folder hiện tại, bỏ qua Scene Segmentation.");
                task.Step3Status = "Done";
                return;
            }

            string gemId = taskModel.SelectedSceneCreatorGem?.Id ?? string.Empty;
            string gemName = taskModel.SelectedSceneCreatorGem?.Name ?? string.Empty;
            string model = GeminiApiService.ResolveModelName(taskModel.SceneCreatorModel);

            var mode = taskModel.UseApiStreamForSceneCreator
                ? SceneSegmentationStep.SegmentationMode.ApiStream
                : SceneSegmentationStep.SegmentationMode.Playwright;

            bool enableThinking = model.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
                                  model.Contains("advanced", StringComparison.OrdinalIgnoreCase);

            await _sceneSegmentationStep.ExecuteAsync(
                task: task,
                logTask: log,
                selectedModel: model,
                outputDir: outputDir,
                gemId: string.IsNullOrWhiteSpace(gemId) ? null : gemId,
                sessionId: null,
                gemName: gemName,
                mode: mode,
                enableExtendedThinking: enableThinking);
        }

        private async Task RunStageImagePromptGenAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            // Check if scenes.json already has image_prompts
            if (taskModel.SkipImagePromptGen && File.Exists(scenesPath))
            {
                if (HasValidImagePrompts(scenesPath))
                {
                    log(task, $"[STAGE-D] ⏭️ scenes.json đã có image_prompts (SkipImagePromptGen=true), bỏ qua Image Prompt Generation.");
                    task.Step4Status = "Done";
                    return;
                }
            }

            // Also check for valid scenes.json
            if (File.Exists(scenesPath) && new FileInfo(scenesPath).Length > 50 && HasValidImagePrompts(scenesPath))
            {
                log(task, $"[STAGE-D] ⏭️ scenes.json hợp lệ đã tồn tại trong folder hiện tại, bỏ qua Image Prompt Generation.");
                task.Step4Status = "Done";
                return;
            }

            string scenesRawPath = Path.Combine(outputDir, "scenes_raw.json");
            if (!File.Exists(scenesRawPath))
            {
                log(task, $"[STAGE-D] ⚠️ scenes_raw.json không tồn tại, bỏ qua Image Prompt Generation.");
                task.Step4Status = "Done";
                return;
            }

            string gemId = taskModel.SelectedImagePromptGem?.Id ?? string.Empty;
            string gemName = taskModel.SelectedImagePromptGem?.Name ?? string.Empty;
            string model = GeminiApiService.ResolveModelName(taskModel.ImagePromptModel);

            var mode = taskModel.UseApiStreamForImagePrompt
                ? ImagePromptGenerationStep.ImagePromptMode.ApiStream
                : ImagePromptGenerationStep.ImagePromptMode.Playwright;

            bool enableThinking = model.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
                                  model.Contains("advanced", StringComparison.OrdinalIgnoreCase);

            await _imagePromptStep.ExecuteAsync(
                task: task,
                logTask: log,
                selectedModel: model,
                outputDir: outputDir,
                gemId: string.IsNullOrWhiteSpace(gemId) ? null : gemId,
                sessionId: null,
                gemName: gemName,
                mode: mode,
                enableExtendedThinking: enableThinking);
        }

        private async Task RunStageImageGenAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            if (!File.Exists(scenesPath))
            {
                log(task, $"[STAGE-D] ⚠️ scenes.json không tồn tại, bỏ qua Image Generation.");
                return;
            }

            bool allImagesAlreadyOnDisk = AreAllSceneImagesGenerated(scenesPath, outputDir);
            if (allImagesAlreadyOnDisk)
            {
                log(task, $"[STAGE-D] ⏭️ Tất cả scene images đã tồn tại, bỏ qua Image Gen. " +
                          "Vẫn đồng bộ BatchProject trong Batch Image Gen tab để có thể retry nếu cần.");
            }

            // Pre-flight health check so we fail fast if the local Python Flow server
            // is down, instead of letting every scene request inside SceneImageBatchStep
            // time out individually.
            //
            // NOTE: taskModel.SelectedImageProvider is the provider KEY (e.g. "flow_local"),
            // NOT a URL. The actual base URL lives in AppSettings.ImageApiUrl — that's
            // what TestHealthAsync expects. TestHealthAsync strips any trailing /v1
            // before probing /health on the root.
            //
            // Skip the health probe when all images already exist (we won't be calling
            // the API this run — SceneImageBatchStep just syncs the BatchProject).
            if (!allImagesAlreadyOnDisk)
            {
                string flowBaseUrl = string.IsNullOrWhiteSpace(_configService.CurrentSettings.ImageApiUrl)
                    ? "http://127.0.0.1:8787/v1"
                    : _configService.CurrentSettings.ImageApiUrl;
                log(task, $"[STAGE-D] 🔎 Pinging Flow Local health at '{flowBaseUrl}'...");
                bool healthy = await _batchImageGenService.TestHealthAsync(flowBaseUrl);
                log(task, healthy
                    ? "[STAGE-D] ✅ Flow Local API /health OK."
                    : $"[STAGE-D] ⚠️ Flow Local API /health không phản hồi (đã ping '{flowBaseUrl}'). Kiểm tra server python ở port 8787 hoặc vào Settings để bật lại.");
                if (!healthy)
                {
                    throw new InvalidOperationException(
                        $"[STAGE-D] ❌ Flow Local API /health failed for '{flowBaseUrl}'. Hãy chắc chắn server python đã chạy (port 8787) hoặc vào Settings để bật lại.");
                }
            }

            // Always invoke the BatchImageGen step so the BatchProject is created or
            // refreshed in the Batch Image Gen tab on every pipeline run — even when
            // 100% of scenes already have images on disk. SceneImageBatchStep itself
            // is idempotent (reuses existing project + skips already-done scenes).
            await _imageBatchStep.ExecuteAsync(
                outputDir: outputDir,
                providerKey: taskModel.SelectedImageProvider,
                task: task,
                logTask: log);

            // Only sleep between real generation bursts. Skip the 15s cool-down when
            // we didn't actually call the API.
            if (!allImagesAlreadyOnDisk)
            {
                log(task, $"[STAGE-D] 😴 Hoàn thành tạo ảnh. Nghỉ 15 giây để GPU hạ nhiệt...");
                await Task.Delay(15_000);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private AutomationTask BuildInternalAutomationTask(GeminiTaskModel taskModel)
        {
            string targetLang = !string.IsNullOrWhiteSpace(taskModel.TargetLanguage)
                ? taskModel.TargetLanguage
                : "English - en";

            var task = new AutomationTask
            {
                VideoUrl = taskModel.Topic.Trim(),
                VoiceId = taskModel.VoiceId.Trim(),
                TargetLanguage = targetLang,
                CharacterRef = taskModel.CharacterRef,
                Step1 = false,
                Step2 = true,   // Deep Research Transcript
                Step3 = true,   // Scene Segmentation (NEW)
                Step4 = true,   // Image Prompt Generation (NEW)
                Step5 = true,   // Batch Image Gen
                Step6 = true,   // Legacy compatibility
                StepSrt = true
            };

            if (string.IsNullOrEmpty(taskModel.OutputFolderName))
            {
                taskModel.OutputFolderName = YoutubeHelper.ToSafeTopicSlug(taskModel.Topic.Trim());
            }
            task.OutputFolderOverride = taskModel.OutputFolderName;

            return task;
        }

        private static bool IsValidScenesJson(string scenesPath)
        {
            try
            {
                string json = File.ReadAllText(scenesPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<ScenesJsonRootModel>(json, options);
                return root != null && root.scenes != null && root.scenes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidScenesRawJson(string scenesRawPath)
        {
            try
            {
                string json = File.ReadAllText(scenesRawPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<SceneSegmentationModel>(json, options);
                return root != null && root.scenes != null && root.scenes.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasValidImagePrompts(string scenesPath)
        {
            try
            {
                string json = File.ReadAllText(scenesPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<ScenesJsonRootModel>(json, options);
                if (root?.scenes == null || root.scenes.Count == 0) return false;

                // Check if at least one scene has an image_prompt
                return root.scenes.Any(s => !string.IsNullOrWhiteSpace(s.image_prompt));
            }
            catch
            {
                return false;
            }
        }

        private static bool AreAllSceneImagesGenerated(string scenesPath, string outputDir)
        {
            try
            {
                string json = File.ReadAllText(scenesPath);
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = System.Text.Json.JsonSerializer.Deserialize<ScenesJsonRootModel>(json, options);
                if (root?.scenes == null || root.scenes.Count == 0) return false;

                string imgDir = Path.Combine(outputDir, "img");
                foreach (var scene in root.scenes)
                {
                    string sceneId = string.IsNullOrWhiteSpace(scene.id) ? $"scene_{scene.scene:D3}" : scene.id;
                    string expectedFile = Path.Combine(imgDir, $"{sceneId}.png");
                    if (!File.Exists(expectedFile))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}