using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Models;
using AssetAutomator.Models.Nodes;

namespace AssetAutomator.Services
{
    /// <summary>
    /// Pipeline Orchestrator — ma trận hàng ngang (tasks) × hàng dọc (stages) với giới hạn slot riêng từng stage.
    /// 
    /// Mô hình Assembly Line:
    ///   - Mỗi task chạy độc lập qua 4 stage tuần tự.
    ///   - Mỗi stage có SemaphoreSlim riêng giới hạn số task đồng thời.
    ///   - Khi 1 task hoàn thành stage A, nó lập tức vào stage B (nếu còn slot), nhường slot A cho task khác.
    ///
    /// Slot mặc định:
    ///   Deep Research : 2
    ///   Voiceover     : 3
    ///   Scene Creator : 4
    ///   Image Gen     : 1
    /// </summary>
    public class PipelineOrchestrator
    {
        private readonly GeminiVideoPipelineService _pipelineService;
        private readonly GeminiApiService _geminiApiService;

        private readonly int _maxDeepResearch;
        private readonly int _maxVoiceover;
        private readonly int _maxSceneCreator;
        private readonly int _maxImageGen;

        /// <summary>
        /// Creates a PipelineOrchestrator with configurable concurrency per stage.
        /// </summary>
        public PipelineOrchestrator(
            GeminiVideoPipelineService pipelineService,
            GeminiApiService geminiApiService,
            int maxDeepResearch = 2,
            int maxVoiceover = 3,
            int maxSceneCreator = 4,
            int maxImageGen = 1)
        {
            _pipelineService = pipelineService;
            _geminiApiService = geminiApiService;
            _maxDeepResearch = Math.Max(1, maxDeepResearch);
            _maxVoiceover = Math.Max(1, maxVoiceover);
            _maxSceneCreator = Math.Max(1, maxSceneCreator);
            _maxImageGen = Math.Max(1, maxImageGen);
        }

        /// <summary>
        /// Thực thi batch Gemini tasks với ma trận pipeline.
        /// Tất cả task khởi động cùng lúc, mỗi task tự đi qua 4 stage với semaphore riêng.
        /// </summary>
        public async Task<PipelineBatchResult> ExecuteBatchAsync(
            List<GeminiTaskItem> taskItems,
            Action<GeminiTaskItem, string> logTask,
            CancellationToken cancellationToken = default)
        {
            var result = new PipelineBatchResult();
            var startedAt = DateTime.Now;

            using var deepResearchSem = new SemaphoreSlim(_maxDeepResearch, _maxDeepResearch);
            using var voiceoverSem = new SemaphoreSlim(_maxVoiceover, _maxVoiceover);
            using var sceneCreatorSem = new SemaphoreSlim(_maxSceneCreator, _maxSceneCreator);
            using var imageGenSem = new SemaphoreSlim(_maxImageGen, _maxImageGen);

            // ── Ma trận: mỗi task là 1 hàng, chạy song song ──
            var taskRunners = taskItems.Select(taskItem =>
                ProcessOneTaskThroughPipelineAsync(
                    taskItem,
                    deepResearchSem,
                    voiceoverSem,
                    sceneCreatorSem,
                    imageGenSem,
                    logTask,
                    cancellationToken
                ));

            await Task.WhenAll(taskRunners);

            result.TotalTasks = taskItems.Count;
            result.SuccessCount = taskItems.Count(t => t.Status == NodeStatus.Success);
            result.FailedCount = taskItems.Count(t => t.Status == NodeStatus.Failed);
            result.Elapsed = DateTime.Now - startedAt;
            return result;
        }

        /// <summary>
        /// Xử lý MỘT task qua toàn bộ pipeline 4 stage với slot giới hạn từng stage.
        /// Đây là 1 hàng trong ma trận.
        /// </summary>
        private async Task ProcessOneTaskThroughPipelineAsync(
            GeminiTaskItem taskItem,
            SemaphoreSlim deepResearchSem,
            SemaphoreSlim voiceoverSem,
            SemaphoreSlim sceneCreatorSem,
            SemaphoreSlim imageGenSem,
            Action<GeminiTaskItem, string> logTask,
            CancellationToken ct)
        {
            string topic = taskItem.Topic.Trim();
            if (string.IsNullOrWhiteSpace(topic))
            {
                taskItem.Status = NodeStatus.Failed;
                taskItem.CurrentStepInfo = "Lỗi: Chưa nhập topic";
                logTask(taskItem, $"[ERROR] Task bị bỏ qua vì chưa có topic.");
                return;
            }

            taskItem.Status = NodeStatus.Running;
            logTask(taskItem, $"[PIPELINE] 🚀 Task '{topic}' vào hàng đợi pipeline...");

            // ── Chuẩn bị AutomationTask nội bộ ──
            var internalTask = BuildInternalAutomationTask(taskItem);

            // Wrap log để forward vào GeminiTaskItem
            Action<AutomationTask, string> internalLog = (t, msg) =>
            {
                logTask(taskItem, msg);
                UpdateTaskItemFromLog(taskItem, msg);
            };

            try
            {
                // ═══════════════════════════════════════════════
                // STAGE A: Deep Research & Transcript (max 2)
                // ═══════════════════════════════════════════════
                await deepResearchSem.WaitAsync(ct);
                try
                {
                    logTask(taskItem, $"[STAGE-A] 🔍 Bắt đầu Deep Research... (đang dùng {_maxDeepResearch - deepResearchSem.CurrentCount}/{_maxDeepResearch} slot)");
                    taskItem.Step1Status = NodeStatus.Running;
                    taskItem.CurrentStepInfo = "Stage A: Deep Research";

                    await RunStageDeepResearchAsync(internalTask, taskItem, internalLog);
                    taskItem.Step1Status = NodeStatus.Success;
                    logTask(taskItem, $"[STAGE-A] ✅ Deep Research hoàn thành. (slot còn trống: {deepResearchSem.CurrentCount})");
                }
                finally
                {
                    deepResearchSem.Release();
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE B: Voiceover & SRT (max 3)
                // ═══════════════════════════════════════════════
                await voiceoverSem.WaitAsync(ct);
                try
                {
                    logTask(taskItem, $"[STAGE-B] 🎙️ Bắt đầu Voiceover AI84... (đang dùng {_maxVoiceover - voiceoverSem.CurrentCount}/{_maxVoiceover} slot)");
                    taskItem.Step2Status = NodeStatus.Running;
                    taskItem.CurrentStepInfo = "Stage B: Voiceover AI84";

                    await RunStageVoiceoverAsync(internalTask, taskItem, internalLog);
                    taskItem.Step2Status = NodeStatus.Success;
                    logTask(taskItem, $"[STAGE-B] ✅ Voiceover hoàn thành. (slot còn trống: {voiceoverSem.CurrentCount})");
                }
                finally
                {
                    voiceoverSem.Release();
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE C: Scene Creator (max 4)
                // ═══════════════════════════════════════════════
                await sceneCreatorSem.WaitAsync(ct);
                try
                {
                    logTask(taskItem, $"[STAGE-C] 🎬 Bắt đầu Scene Creator... (đang dùng {_maxSceneCreator - sceneCreatorSem.CurrentCount}/{_maxSceneCreator} slot)");
                    taskItem.Step3Status = NodeStatus.Running;
                    taskItem.CurrentStepInfo = "Stage C: Scene Creator";

                    await RunStageSceneCreatorAsync(internalTask, taskItem, internalLog);
                    taskItem.Step3Status = NodeStatus.Success;
                    logTask(taskItem, $"[STAGE-C] ✅ Scene Creator hoàn thành. (slot còn trống: {sceneCreatorSem.CurrentCount})");
                }
                finally
                {
                    sceneCreatorSem.Release();
                }

                ct.ThrowIfCancellationRequested();

                // ═══════════════════════════════════════════════
                // STAGE D: Image Generation (max 1 — tuần tự)
                // ═══════════════════════════════════════════════
                await imageGenSem.WaitAsync(ct);
                try
                {
                    logTask(taskItem, $"[STAGE-D] 🖼️ Bắt đầu Image Generation... (đang dùng {_maxImageGen - imageGenSem.CurrentCount}/{_maxImageGen} slot)");
                    taskItem.Step4Status = NodeStatus.Running;
                    taskItem.CurrentStepInfo = "Stage D: Image Gen";

                    await RunStageImageGenAsync(internalTask, taskItem, internalLog);
                    taskItem.Step4Status = NodeStatus.Success;
                    logTask(taskItem, $"[STAGE-D] ✅ Image Generation hoàn thành. (slot còn trống: {imageGenSem.CurrentCount})");
                }
                finally
                {
                    imageGenSem.Release();
                }

                // ── Hoàn thành ──
                taskItem.Status = NodeStatus.Success;
                taskItem.CurrentStepInfo = "✔️ Hoàn thành 100%";
                logTask(taskItem, $"[PIPELINE] 🎉 Task '{topic}' hoàn thành toàn bộ pipeline!");
            }
            catch (OperationCanceledException)
            {
                taskItem.Status = NodeStatus.Failed;
                taskItem.CurrentStepInfo = "⏹️ Đã hủy";
                logTask(taskItem, $"[PIPELINE] ⏹️ Task '{topic}' bị hủy.");
            }
            catch (Exception ex)
            {
                taskItem.Status = NodeStatus.Failed;
                taskItem.CurrentStepInfo = $"❌ Lỗi: {ex.Message}";
                logTask(taskItem, $"[PIPELINE] ❌ Task '{topic}' thất bại: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────
        //  Stage runners — gọi từng step riêng biệt
        // ─────────────────────────────────────────────────────

        private async Task RunStageDeepResearchAsync(
            AutomationTask task, GeminiTaskItem taskItem,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            Directory.CreateDirectory(outputDir);
            string transcriptPath = Path.Combine(outputDir, "transcript.txt");

            // Skip nếu đã có transcript
            if (File.Exists(transcriptPath) && new FileInfo(transcriptPath).Length > 50)
            {
                log(task, $"[STAGE-A] ⏭️ transcript.txt đã tồn tại, bỏ qua Deep Research.");
                task.Step2Status = "Completed";
                return;
            }

            var topicResearchStep = new GeminiTopicResearchStep(_geminiApiService);
            await topicResearchStep.ExecuteAsync(
                topicOrUrl: taskItem.Topic.Trim(),
                outputDir: outputDir,
                gemId: taskItem.SelectedScriptwriterGem?.id,
                enableDeepResearch: taskItem.EnableDeepResearch,
                task: task,
                logTask: log,
                selectedModel: taskItem.SelectedModel ?? "gemini-3-flash",
                selectedExtension: taskItem.SelectedExtension ?? "None",
                existingSessionId: null
            );
        }

        private async Task RunStageVoiceoverAsync(
            AutomationTask task, GeminiTaskItem taskItem,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string mp3Path = Path.Combine(outputDir, "voiceover.mp3");
            string wavPath = Path.Combine(outputDir, "voiceover.wav");
            string srtPath = Path.Combine(outputDir, "voiceover.srt");

            bool hasAudio = (File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 1000)
                         || (File.Exists(wavPath) && new FileInfo(wavPath).Length > 1000);
            bool hasSrt = File.Exists(srtPath) && new FileInfo(srtPath).Length > 10;

            if (hasAudio && hasSrt)
            {
                log(task, $"[STAGE-B] ⏭️ voiceover.mp3 và voiceover.srt đã tồn tại, bỏ qua Voiceover.");
                task.Step4Status = "Completed";
                task.StepSrtStatus = "Completed";
                return;
            }

            string transcriptPath = Path.Combine(outputDir, "transcript.txt");
            string scriptText = File.Exists(transcriptPath)
                ? await File.ReadAllTextAsync(transcriptPath)
                : string.Empty;

            var voiceoverStep = new VoiceoverGenerationStep();
            await voiceoverStep.ExecuteAsync(
                voiceId: taskItem.VoiceId.Trim(),
                outputDir: outputDir,
                scriptText: scriptText,
                videoId: task.VideoId,
                task: task,
                apiKey: ConfigService.CurrentSettings.Ai84ApiKey,
                logTask: log
            );
        }

        private async Task RunStageSceneCreatorAsync(
            AutomationTask task, GeminiTaskItem taskItem,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            if (File.Exists(scenesPath) && new FileInfo(scenesPath).Length > 50 && IsValidScenesJson(scenesPath))
            {
                log(task, $"[STAGE-C] ⏭️ scenes.json hợp lệ đã tồn tại, bỏ qua Scene Creator.");
                task.Step3Status = "Completed";
                return;
            }

            var sceneBreakdownStep = new GeminiSceneBreakdownStep(_geminiApiService);
            await sceneBreakdownStep.ExecuteAsync(
                outputDir: outputDir,
                gemId: taskItem.SelectedSceneCreatorGem?.id,
                task: task,
                logTask: log,
                selectedModel: taskItem.SelectedModel ?? "gemini-3-flash",
                selectedExtension: taskItem.SelectedExtension ?? "None",
                sessionId: null
            );
        }

        private async Task RunStageImageGenAsync(
            AutomationTask task, GeminiTaskItem taskItem,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            if (!File.Exists(scenesPath))
            {
                log(task, $"[STAGE-D] ⚠️ scenes.json không tồn tại, bỏ qua Image Generation.");
                return;
            }

            // Kiểm tra xem tất cả ảnh đã có chưa
            if (AreAllSceneImagesGenerated(scenesPath, outputDir))
            {
                log(task, $"[STAGE-D] ⏭️ Tất cả scene images đã tồn tại, bỏ qua Image Gen.");
                task.Step5Status = "Completed";
                return;
            }

            var imageBatchStep = new SceneImageBatchStep(new BatchImageGenService());
            await imageBatchStep.ExecuteAsync(
                outputDir: outputDir,
                providerKey: taskItem.SelectedImageProvider ?? "flow_local",
                task: task,
                logTask: log
            );

            // Nghỉ 15 giây để GPU/API hạ nhiệt trước khi task tiếp theo vào Stage D
            log(task, $"[STAGE-D] 😴 Hoàn thành tạo ảnh. Nghỉ 15 giây để GPU hạ nhiệt...");
            await Task.Delay(15_000);
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private AutomationTask BuildInternalAutomationTask(GeminiTaskItem taskItem)
        {
            string targetLang = !string.IsNullOrWhiteSpace(taskItem.TargetLanguage)
                ? taskItem.TargetLanguage
                : "English - en";

            var task = new AutomationTask
            {
                VideoUrl = taskItem.Topic.Trim(),
                VoiceId = taskItem.VoiceId.Trim(),
                TargetLanguage = targetLang,
                Step1 = false,
                Step2 = true,   // Deep Research Transcript
                Step3 = true,   // Scene Breakdown
                Step4 = true,   // Voiceover
                Step5 = true,   // Batch Image Gen
                StepSrt = true
            };

            if (string.IsNullOrEmpty(taskItem.OutputFolderName))
            {
                taskItem.OutputFolderName = YoutubeHelper.ToSafeTopicSlug(taskItem.Topic.Trim());
            }
            task.OutputFolderOverride = taskItem.OutputFolderName;

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

        private void UpdateTaskItemFromLog(GeminiTaskItem taskItem, string msg)
        {
            // Map log messages to step status updates
            if (msg.Contains("DEEP-RESEARCH-POLL", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step1Status = NodeStatus.Running;
                taskItem.CurrentStepInfo = "Stage A: Deep Research (Đang nghiên cứu...)";
            }
            else if (msg.Contains("STAGE-A", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 1", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 2", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step1Status = msg.Contains("✅") ? NodeStatus.Success : NodeStatus.Running;
            }
            else if (msg.Contains("STAGE-B", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 3", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("VOICEOVER", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step2Status = msg.Contains("✅") ? NodeStatus.Success : NodeStatus.Running;
            }
            else if (msg.Contains("STAGE-C", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 4", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Scene Creator", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step3Status = msg.Contains("✅") ? NodeStatus.Success : NodeStatus.Running;
            }
            else if (msg.Contains("STAGE-D", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("STEP 5", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Image Generation", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("Batch Image", StringComparison.OrdinalIgnoreCase))
            {
                taskItem.Step4Status = msg.Contains("✅") ? NodeStatus.Success : NodeStatus.Running;
            }
        }
    }

    /// <summary>
    /// Kết quả thực thi batch pipeline.
    /// </summary>
    public class PipelineBatchResult
    {
        public int TotalTasks { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public TimeSpan Elapsed { get; set; }

        public override string ToString()
        {
            return $"✅ {SuccessCount}/{TotalTasks} thành công, ❌ {FailedCount} thất bại — {Elapsed.TotalMinutes:F1} phút";
        }
    }

    /// <summary>
    /// Wrapper cho một Gemini task item đi qua pipeline.
    /// Tách biệt với GeminiTaskModel để pipeline không phụ thuộc vào UI model.
    /// </summary>
    public class GeminiTaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Topic { get; set; } = string.Empty;
        public string VoiceId { get; set; } = string.Empty;
        public string? TargetLanguage { get; set; }
        public string? OutputFolderName { get; set; }
        public string? SelectedModel { get; set; }
        public string? SelectedExtension { get; set; }
        public string? SelectedImageProvider { get; set; }
        public bool EnableDeepResearch { get; set; }
        public GemModel? SelectedScriptwriterGem { get; set; }
        public GemModel? SelectedSceneCreatorGem { get; set; }

        // Trạng thái
        public NodeStatus Status { get; set; } = NodeStatus.Idle;
        public string CurrentStepInfo { get; set; } = "Đang chờ...";

        public NodeStatus Step1Status { get; set; } = NodeStatus.Idle;
        public NodeStatus Step2Status { get; set; } = NodeStatus.Idle;
        public NodeStatus Step3Status { get; set; } = NodeStatus.Idle;
        public NodeStatus Step4Status { get; set; } = NodeStatus.Idle;

        public string Step1Logs { get; set; } = string.Empty;
        public string Step2Logs { get; set; } = string.Empty;
        public string Step3Logs { get; set; } = string.Empty;
        public string Step4Logs { get; set; } = string.Empty;

        /// <summary>
        /// Tạo GeminiTaskItem từ GeminiTaskModel (UI model).
        /// </summary>
        public static GeminiTaskItem FromGeminiTaskModel(GeminiTaskModel model)
        {
            return new GeminiTaskItem
            {
                Id = model.Id,
                Topic = model.Topic,
                VoiceId = model.VoiceId,
                TargetLanguage = model.TargetLanguage,
                OutputFolderName = model.OutputFolderName,
                SelectedModel = model.SelectedModel,
                SelectedExtension = model.SelectedExtension,
                SelectedImageProvider = model.SelectedImageProvider,
                EnableDeepResearch = model.EnableDeepResearch,
                SelectedScriptwriterGem = GeminiApiService.ConvertFromGemOption(model.SelectedScriptwriterGem),
                SelectedSceneCreatorGem = GeminiApiService.ConvertFromGemOption(model.SelectedSceneCreatorGem),
                Status = model.Status
            };
        }
    }
}
