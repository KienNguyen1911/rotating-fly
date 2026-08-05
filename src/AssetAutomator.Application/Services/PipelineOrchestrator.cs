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
    ///   - Mỗi task chạy độc lập qua 4 stage tuần tự.
    ///   - Mỗi stage có SemaphoreSlim riêng giới hạn số task đồng thời.
    ///   - Khi 1 task hoàn thành stage A, nó lập tức vào stage B (nếu còn slot), nhường slot A cho task khác.
    ///
    /// Slot mặc định:
    ///   Deep Research : 2
    ///   Voiceover     : 3
    ///   Scene Creator : 4
    ///   Image Gen     : 1 (tuần tự tuyệt đối, sleep 15s giữa các task)
    /// </summary>
    public class PipelineOrchestrator
    {
        private readonly GeminiApiService _geminiApiService;
        private readonly IConfigService _configService;
        private readonly VoiceoverGenerationStep _voiceoverStep;
        private readonly GeminiPlaywrightSceneBreakdownStep _sceneBreakdownStep;
        private readonly BatchImageGenService _batchImageGenService;
        private readonly GeminiTopicResearchStep _topicResearchStep;
        private readonly SceneImageBatchStep _imageBatchStep;

        private readonly int _maxDeepResearch;
        private readonly int _maxVoiceover;
        private readonly int _maxSceneCreator;
        private readonly int _maxImageGen;

        public PipelineOrchestrator(
            GeminiApiService geminiApiService,
            IConfigService configService,
            VoiceoverGenerationStep voiceoverStep,
            GeminiPlaywrightSceneBreakdownStep sceneBreakdownStep,
            BatchImageGenService batchImageGenService,
            GeminiTopicResearchStep topicResearchStep,
            SceneImageBatchStep imageBatchStep,
            int maxDeepResearch = 2,
            int maxVoiceover = 3,
            int maxSceneCreator = 4,
            int maxImageGen = 1)
        {
            _geminiApiService = geminiApiService;
            _configService = configService;
            _voiceoverStep = voiceoverStep;
            _sceneBreakdownStep = sceneBreakdownStep;
            _batchImageGenService = batchImageGenService;
            _topicResearchStep = topicResearchStep;
            _imageBatchStep = imageBatchStep;
            _maxDeepResearch = Math.Max(1, maxDeepResearch);
            _maxVoiceover = Math.Max(1, maxVoiceover);
            _maxSceneCreator = Math.Max(1, maxSceneCreator);
            _maxImageGen = Math.Max(1, maxImageGen);
        }

        /// <summary>
        /// Thực thi batch Gemini tasks với ma trận pipeline.
        /// Tất cả task khởi động cùng lúc, mỗi task tự đi qua 4 stage với semaphore riêng.
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
            using var sceneCreatorSem = new SemaphoreSlim(_maxSceneCreator, _maxSceneCreator);
            using var imageGenSem = new SemaphoreSlim(_maxImageGen, _maxImageGen);

            // ── Ma trận: mỗi task là 1 hàng, chạy song song ──
            var taskRunners = taskModels.Select(taskModel =>
                ProcessOneTaskThroughPipelineAsync(
                    taskModel,
                    deepResearchSem,
                    voiceoverSem,
                    sceneCreatorSem,
                    imageGenSem,
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
        /// Xử lý MỘT task qua toàn bộ pipeline 4 stage với slot giới hạn từng stage.
        /// </summary>
        private async Task ProcessOneTaskThroughPipelineAsync(
            GeminiTaskModel taskModel,
            SemaphoreSlim deepResearchSem,
            SemaphoreSlim voiceoverSem,
            SemaphoreSlim sceneCreatorSem,
            SemaphoreSlim imageGenSem,
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

            taskModel.Status = NodeStatus.Running;
            logTask(taskModel, $"[PIPELINE] 🚀 Task '{topic}' vào hàng đợi pipeline...");

            // ── Build internal AutomationTask ──
            var internalTask = BuildInternalAutomationTask(taskModel);

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

                    await RunStageDeepResearchAsync(internalTask, taskModel, internalLog);
                    taskModel.Step1Status = NodeStatus.Success;
                    logTask(taskModel, $"[STAGE-A] ✅ Deep Research hoàn thành.");
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
                    int used = _maxVoiceover - voiceoverSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-B] 🎙️ Bắt đầu Voiceover AI84... (slot {used}/{_maxVoiceover})");
                    taskModel.Step2Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage B: Voiceover AI84 ({used}/{_maxVoiceover})";

                    await RunStageVoiceoverAsync(internalTask, taskModel, internalLog);
                    taskModel.Step2Status = NodeStatus.Success;
                    logTask(taskModel, $"[STAGE-B] ✅ Voiceover hoàn thành.");
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
                    int used = _maxSceneCreator - sceneCreatorSem.CurrentCount;
                    logTask(taskModel, $"[STAGE-C] 🎬 Bắt đầu Scene Creator... (slot {used}/{_maxSceneCreator})");
                    taskModel.Step3Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = $"Stage C: Scene Creator ({used}/{_maxSceneCreator})";

                    await RunStageSceneCreatorAsync(internalTask, taskModel, internalLog);
                    taskModel.Step3Status = NodeStatus.Success;
                    logTask(taskModel, $"[STAGE-C] ✅ Scene Creator hoàn thành.");
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
                    logTask(taskModel, $"[STAGE-D] 🖼️ Bắt đầu Image Generation... (1/1 slot)");
                    taskModel.Step4Status = NodeStatus.Running;
                    taskModel.CurrentStepInfo = "Stage D: Image Gen (1/1)";

                    await RunStageImageGenAsync(internalTask, taskModel, internalLog);
                    taskModel.Step4Status = NodeStatus.Success;
                    logTask(taskModel, $"[STAGE-D] ✅ Image Generation hoàn thành.");
                }
                finally
                {
                    imageGenSem.Release();
                }

                // ── Hoàn thành ──
                taskModel.Status = NodeStatus.Success;
                taskModel.CurrentStepInfo = "✔️ Hoàn thành 100%";
                logTask(taskModel, $"[PIPELINE] 🎉 Task '{topic}' hoàn thành toàn bộ pipeline!");
            }
            catch (OperationCanceledException)
            {
                taskModel.Status = NodeStatus.Failed;
                taskModel.CurrentStepInfo = "⏹️ Đã hủy";
                logTask(taskModel, $"[PIPELINE] ⏹️ Task '{topic}' bị hủy.");
            }
            catch (Exception ex)
            {
                taskModel.Status = NodeStatus.Failed;
                taskModel.CurrentStepInfo = $"❌ Lỗi: {ex.Message}";
                logTask(taskModel, $"[PIPELINE-ERROR] ❌ Task '{topic}' thất bại: {ex.Message}");
            }
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
                existingSessionId: null
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

        private async Task RunStageSceneCreatorAsync(
            AutomationTask task, GeminiTaskModel taskModel,
            Action<AutomationTask, string> log)
        {
            string outputDir = task.OutputDir;
            string scenesPath = Path.Combine(outputDir, "scenes.json");

            // Strict primary-folder check only — do NOT skip when scenes.json only exists in a sibling folder.
            if (File.Exists(scenesPath) && new FileInfo(scenesPath).Length > 50 && IsValidScenesJson(scenesPath))
            {
                log(task, $"[STAGE-C] ⏭️ scenes.json hợp lệ đã tồn tại trong folder hiện tại, bỏ qua Scene Creator.");
                task.Step3Status = "Done";
                return;
            }

            string gemId = taskModel.SelectedSceneCreatorGem?.Id ?? string.Empty;
            string gemName = taskModel.SelectedSceneCreatorGem?.Name ?? string.Empty;
            string model = GeminiApiService.ResolveModelName(taskModel.SceneCreatorModel);

            // Routing: respect the per-task toggle so users can A/B test
            // API Stream (fast, no Chrome) vs Playwright (real Web UI).
            // Default = ApiStream because it mirrors test_gem_and_thinking.py
            // and does not require a Chrome profile with an active session.
            var mode = taskModel.UseApiStreamForSceneCreator
                ? GeminiPlaywrightSceneBreakdownStep.SceneBreakdownMode.ApiStream
                : GeminiPlaywrightSceneBreakdownStep.SceneBreakdownMode.Playwright;

            // Auto-enable extended thinking when the resolved model name
            // already carries the -thinking suffix (e.g. gemini-3-flash-thinking).
            bool enableThinking = model.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
                                  model.Contains("advanced", StringComparison.OrdinalIgnoreCase);

            await _sceneBreakdownStep.ExecuteAsync(
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

            if (AreAllSceneImagesGenerated(scenesPath, outputDir))
            {
                log(task, $"[STAGE-D] ⏭️ Tất cả scene images đã tồn tại, bỏ qua Image Gen.");
                task.Step5Status = "Done";
                return;
            }

            // Pre-flight health check so we fail fast if the local Python Flow server
            // is down, instead of letting every scene request inside SceneImageBatchStep
            // time out individually.
            //
            // NOTE: taskModel.SelectedImageProvider is the provider KEY (e.g. "flow_local"),
            // NOT a URL. The actual base URL lives in AppSettings.ImageApiUrl — that's
            // what TestHealthAsync expects. TestHealthAsync strips any trailing /v1
            // before probing /health on the root.
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

            // Actually run image generation. Previously this stage was a stub that only
            // pinged /health and slept 15s — that left scenes.json in place but no
            // files in img/, while Step4Status was still marked Success.
            await _imageBatchStep.ExecuteAsync(
                outputDir: outputDir,
                providerKey: taskModel.SelectedImageProvider,
                task: task,
                logTask: log);

            // Nghỉ 15 giây để GPU/API hạ nhiệt
            log(task, $"[STAGE-D] 😴 Hoàn thành tạo ảnh. Nghỉ 15 giây để GPU hạ nhiệt...");
            await Task.Delay(15_000);
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
                Step3 = true,   // Scene Breakdown
                Step4 = true,   // Voiceover
                Step5 = true,   // Batch Image Gen
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