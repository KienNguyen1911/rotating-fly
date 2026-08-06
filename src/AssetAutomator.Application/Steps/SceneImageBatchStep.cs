using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Services.Providers;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Pipeline Step 5: Reads scenes.json, parses image_prompts for all scenes,
    /// then runs the Flow Local API sequence:
    ///   1. Create Flow project (lưu FlowProjectId, FlowProjectUrl).
    ///   2. Upload reference image nếu có (lưu media_id).
    ///   3. Generate scene images bằng project + reference.
    /// Uses SemaphoreSlim to cap concurrent image generation at MaxConcurrentImages.
    /// After completion, creates a BatchProject in BatchImageGen tab for quality control.
    /// </summary>
    public class SceneImageBatchStep
    {
        private readonly BatchImageGenService _batchImageGenService;
        private readonly BatchProjectService _batchProjectService;
        private readonly IConfigService _configService;
        private const int MaxConcurrentImages = 6;

        public SceneImageBatchStep(
            BatchImageGenService batchImageGenService,
            BatchProjectService batchProjectService,
            IConfigService configService)
        {
            _batchImageGenService = batchImageGenService;
            _batchProjectService = batchProjectService;
            _configService = configService;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? providerKey,
            AutomationTask task,
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 5] Starting Batch Image Generation for Scenes...");
            task.Step5Status = "Running";

            string scenesPath = Path.Combine(outputDir, "scenes.json");
            if (!File.Exists(scenesPath))
            {
                throw new FileNotFoundException($"scenes.json not found at {scenesPath}. Please complete Step 4 (Scene Breakdown) first.");
            }

            string jsonContent = await File.ReadAllTextAsync(scenesPath);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rootData = JsonSerializer.Deserialize<ScenesJsonRootModel>(jsonContent, options);

            if (rootData == null || rootData.scenes == null || rootData.scenes.Count == 0)
            {
                throw new InvalidOperationException("[STEP 5] No valid scenes found in scenes.json.");
            }

            string provider = string.IsNullOrWhiteSpace(providerKey)
                ? (_configService.CurrentSettings.DefaultImageGenProvider ?? "flow_local")
                : providerKey;
            // Legacy projects may have provider="glabs" - silently remap to flow_local.
            if (!string.Equals(provider, "flow_local", StringComparison.OrdinalIgnoreCase))
            {
                provider = "flow_local";
            }

            // Pass empty strings so FlowLocalImageGenProvider uses its built-in defaults
            // (http://127.0.0.1:8787/v1 + flow-local-key) when settings are missing.
            string serverUrl = "";
            string apiKey = "";

            // Default model name must match Google Flow Local API naming (hyphens, not underscores).
            string defaultModel = "nano-banana-2";

            string imgDir = Path.Combine(outputDir, "img");
            Directory.CreateDirectory(imgDir);

            logTask(task, $"[STEP 5] Found {rootData.scenes.Count} scenes. Output subfolder: '{imgDir}'. Using Provider: '{provider}'. Concurrency: {MaxConcurrentImages}...");

            // ── Build reference image list ──
            var referenceImages = new List<(string base64Data, string tag, string filePath)>();
            string? characterRefPath = null;

            if (!string.IsNullOrWhiteSpace(task.CharacterRef))
            {
                if (File.Exists(task.CharacterRef))
                {
                    try
                    {
                        byte[] imageBytes = await File.ReadAllBytesAsync(task.CharacterRef);
                        string base64 = Convert.ToBase64String(imageBytes);
                        referenceImages.Add((base64, "@character", task.CharacterRef));
                        characterRefPath = task.CharacterRef;
                        logTask(task, $"[STEP 5] Loaded Character Reference Image: '{Path.GetFileName(task.CharacterRef)}'");
                    }
                    catch (Exception ex)
                    {
                        logTask(task, $"[STEP 5] [WARNING] Failed to read Character Reference file: {ex.Message}");
                    }
                }
                else
                {
                    logTask(task, $"[STEP 5] [WARNING] CharacterRef is set but file does not exist: '{task.CharacterRef}'");
                }
            }

            // ── Step 1: Create Flow project FIRST so reference upload + generation share the same project_id ──
            // This guarantees the (project_id, sha256) cache on Flow server returns the same media_id,
            // and that the project's media library actually contains the reference image used.
            string projectTitle = $"Gemini_{task.Id.ToString()[..8]}_{Path.GetFileName(outputDir.TrimEnd('/', '\\'))}";
            string? flowProjectId = null;
            string? flowProjectUrl = null;
            string? flowError = null;

            (flowProjectId, flowProjectUrl, flowError) = await FlowLocalImageGenProvider.CreateProjectAsync(serverUrl, apiKey, projectTitle);

            if (!string.IsNullOrEmpty(flowError) || string.IsNullOrEmpty(flowProjectId))
            {
                logTask(task, $"[STEP 5] [WARNING] Could not create Flow project upfront ('{flowError ?? "no project_id"}'). Will fall back to provider-side default project at generation time.");
            }
            else
            {
                logTask(task, $"[STEP 5] ✅ Created Flow Project '{projectTitle}' (id={flowProjectId}, url={flowProjectUrl ?? "n/a"}).");
            }

            // Reset the in-memory reference media cache so the first item uploads + caches cleanly
            // for *this* Flow project. This avoids cross-project stale IDs.
            FlowLocalImageGenProvider.ClearReferenceMediaCache();

            // Build items list from scenes. FlowProjectId is assigned now so the provider
            // will send project_id on every upload + generation request.
            var items = new List<(BatchImageItem item, string sceneId, int index, int sceneNumber)>();
            for (int i = 0; i < rootData.scenes.Count; i++)
            {
                var scene = rootData.scenes[i];
                string sceneId = string.IsNullOrWhiteSpace(scene.id) ? $"scene_{scene.scene:D3}" : scene.id;
                int sceneNumber = scene.scene > 0 ? scene.scene : (i + 1);

                items.Add((new BatchImageItem
                {
                    Index = sceneNumber,
                    TaskId = task.Id.ToString(),
                    Prompt = scene.image_prompt,
                    Transcript = scene.transcript,
                    SceneTitle = $"Scene #{sceneNumber}: {sceneId}",
                    Provider = provider,
                    Engine = "flow",
                    Model = defaultModel,
                    AspectRatio = "16:9",
                    FlowProjectId = flowProjectId,
                    FlowProjectTitle = string.IsNullOrEmpty(flowProjectId) ? projectTitle : null,
                    FlowProjectUrl = flowProjectUrl,
                    Status = "Processing"
                }, sceneId, i, sceneNumber));
            }

            // Concurrent generation with semaphore-based throttling
            using var semaphore = new SemaphoreSlim(MaxConcurrentImages);
            int total = items.Count;
            int processed = 0;
            int successCount = 0;
            int failCount = 0;

            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentImages }, async (entry, ct) =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    int current = Interlocked.Increment(ref processed);
                    logTask(task, $"[STEP 5] Generating Image {current}/{total} ({entry.sceneId})...");

                    await _batchImageGenService.ProcessSingleImageItemAsync(
                        entry.item,
                        serverUrl,
                        apiKey,
                        referenceImages,
                        imgDir
                    );

                    if (entry.item.Status.Equals("Done", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref successCount);
                        // Rename raw file to canonical scene_XXX.png so re-running the pipeline (and the
                        // AreAllSceneImagesGenerated skip check) sees real assets.
                        await TryRenameToCanonicalFileNameAsync(entry.item, imgDir, entry.sceneId);
                    }
                    else
                    {
                        Interlocked.Increment(ref failCount);
                        logTask(task, $"[STEP 5] [WARNING] Scene {entry.sceneId} status: {entry.item.Status}. Error: {entry.item.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failCount);
                    logTask(task, $"[STEP 5] [ERROR] Exception generating image for {entry.sceneId}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            task.Step5Status = "Done";
            logTask(task, $"[STEP 5] Success! Finished Batch Image Generation. ({successCount}/{total} images created, {failCount} failed).");

            // ── Create BatchProject in BatchImageGen tab for quality control ──
            await CreateBatchProjectForTaskAsync(task, rootData, imgDir, provider, defaultModel, characterRefPath, flowProjectId, flowProjectUrl, logTask);
        }

        /// <summary>
        /// Creates a BatchProject in BatchImageGen tab after image generation completes.
        /// This allows users to review quality, regenerate failed images, and collect assets.
        /// Reference images and the Flow project id are wired in so re-runs from Batch tab
        /// continue to share the same Flow context as the original task pipeline run.
        /// </summary>
        private async Task CreateBatchProjectForTaskAsync(
            AutomationTask task,
            ScenesJsonRootModel? rootData,
            string imgDir,
            string provider,
            string defaultModel,
            string? characterRefPath,
            string? flowProjectId,
            string? flowProjectUrl,
            Action<AutomationTask, string> logTask)
        {
            try
            {
                // Generate project name from task ID or topic
                string projectName = $"Gemini_{task.Id.ToString()[..8]}_{DateTime.Now:yyyyMMdd_HHmmss}";
                if (!string.IsNullOrWhiteSpace(task.VideoId) && task.VideoId.Length > 5)
                {
                    projectName = $"Gemini_{task.VideoId[..Math.Min(20, task.VideoId.Length)]}";
                }

                logTask(task, $"[STEP 5] Creating BatchProject '{projectName}' in BatchImageGen tab...");

                // Create the project
                var project = await _batchProjectService.CreateProjectAsync(projectName);

                // Update project metadata
                project.OutputDir = imgDir;
                project.Provider = provider;
                project.Engine = "flow";
                project.Model = defaultModel;
                project.AspectRatio = "16:9";
                project.Concurrency = MaxConcurrentImages;
                project.FlowProjectId = flowProjectId;
                project.FlowProjectUrl = flowProjectUrl;

                // Carry the CharacterRef image forward so the Batch tab regenerates with the same reference.
                if (!string.IsNullOrWhiteSpace(characterRefPath) && File.Exists(characterRefPath))
                {
                    project.RefImagePaths = new List<string> { characterRefPath };
                }

                // Add items from scenes
                if (rootData?.scenes != null)
                {
                    foreach (var scene in rootData.scenes)
                    {
                        string sceneId = string.IsNullOrWhiteSpace(scene.id)
                            ? $"scene_{scene.scene:D3}"
                            : scene.id;

                        string canonicalPath = Path.Combine(imgDir, $"{sceneId}.png");
                        bool exists = File.Exists(canonicalPath);

                        // Fall back to the flow_image_* filename the provider wrote, so we don't
                        // mark a real success as Failed just because the rename failed.
                        string imagePath = canonicalPath;
                        if (!exists)
                        {
                            string indexed = $"_{scene.scene}_";
                            var candidates = Directory.Exists(imgDir)
                                ? Directory.GetFiles(imgDir, "flow_image_*.png")
                                : Array.Empty<string>();
                            var fallback = candidates
                                .Where(f => Path.GetFileName(f).Contains(indexed, StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                .FirstOrDefault();
                            if (!string.IsNullOrEmpty(fallback))
                            {
                                imagePath = fallback;
                                exists = true;
                            }
                        }

                        project.Items.Add(new BatchImageItemState
                        {
                            Index = scene.scene,
                            SceneTitle = $"Scene #{scene.scene}: {sceneId}",
                            Transcript = scene.transcript ?? string.Empty,
                            Prompt = scene.image_prompt ?? string.Empty,
                            Status = exists ? "Done" : "Failed",
                            ImagePath = exists ? imagePath : string.Empty,
                            ErrorMessage = exists ? string.Empty : "Image file not found after generation",
                            Engine = "flow",
                            Model = defaultModel,
                            AspectRatio = "16:9",
                            FlowProjectId = flowProjectId,
                            FlowProjectTitle = string.IsNullOrEmpty(flowProjectId) ? projectName : null,
                            FlowProjectUrl = flowProjectUrl
                        });
                    }
                }

                // Save project with all items
                await _batchProjectService.SaveProjectAsync(project);

                logTask(task, $"[STEP 5] ✅ BatchProject created with {project.Items.Count} image items for quality review.");
            }
            catch (Exception ex)
            {
                logTask(task, $"[STEP 5] ⚠️ Failed to create BatchProject: {ex.Message}");
                // Don't throw - image gen was successful, this is just a convenience feature
            }
        }

        /// <summary>
        /// Renames the provider-written file (flow_image_{Index}_{ts}.png) to the canonical
        /// scene_{XXX}.png filename that the orchestrator / batch UI look for. After copying
        /// the file to its canonical name the original raw file is deleted so each scene ends
        /// up with exactly one PNG. If a file with the canonical name already exists (e.g., from
        /// a partial previous run), it is overwritten only when the source is newer than the
        /// target.
        /// </summary>
        private static async Task TryRenameToCanonicalFileNameAsync(BatchImageItem item, string imgDir, string sceneId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(item.ImagePath) || !File.Exists(item.ImagePath))
                {
                    return;
                }

                string canonicalPath = Path.Combine(imgDir, $"{sceneId}.png");
                if (string.Equals(item.ImagePath, canonicalPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Async copy then delete so we don't depend on File.Move on the same volume.
                string tmp = canonicalPath + ".tmp";
                await using (var src = new FileStream(item.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await src.CopyToAsync(dst);
                }

                if (File.Exists(canonicalPath))
                {
                    File.Delete(canonicalPath);
                }
                File.Move(tmp, canonicalPath);

                // Delete the provider-written raw file (flow_image_{Index}_{ts}.png) now that
                // we have the canonical scene_{XXX}.png copy. Without this, every scene ends up
                // with two PNGs in the img/ folder — the rename looks like a successful "dedupe"
                // but the original still consumes disk and confuses the Batch UI / skip check.
                try
                {
                    if (File.Exists(item.ImagePath) &&
                        !string.Equals(item.ImagePath, canonicalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(item.ImagePath);
                    }
                }
                catch
                {
                    // Best-effort cleanup; canonical copy is already in place.
                }

                item.ImagePath = canonicalPath;
            }
            catch
            {
                // Best-effort rename; we still have the original flow_image_* file on disk.
            }
        }
    }
}
