using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Application.Services.Providers;
using AssetAutomator.Core.Constants;
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
    ///
    /// IMPORTANT: Before any image generation, this step ALWAYS creates (or
    /// reuses) a BatchProject in the Batch Image Gen tab so the user can:
    ///   • Review which scenes failed (HTTP 402 / 429 / etc.).
    ///   • Re-trigger single-item generation via the Batch UI using the
    ///     preserved FlowProjectId + ReferenceMediaId cache.
    ///
    /// Project creation is idempotent: re-running the same pipeline task
    /// does NOT spawn duplicate projects and does NOT reset the previously
    /// captured <c>FlowProjectId</c> / <c>FlowProjectUrl</c>. Items whose
    /// image file already exists on disk are skipped so only the missing
    /// scenes are sent to Flow Local.
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

            // ── Resolve BatchProject name from the original topic. ──
            // We use the raw topic for the user-facing "ProjectName" so the Batch
            // Image Gen dashboard shows the topic as-is (e.g. "Sunday Scaries"),
            // while the folder name is the sanitized slug ("sunday-scaries").
            // The slug is used as the directory key inside BatchProjectService so
            // re-running the same pipeline task lands on the same project folder.
            string rawTopic = !string.IsNullOrWhiteSpace(task.VideoUrl)
                ? task.VideoUrl
                : Path.GetFileName(outputDir.TrimEnd('/', '\\'));
            string batchProjectName = BatchProjectService.SanitizeTopicAsProjectName(rawTopic);
            // Display name keeps spaces + capitalization; falls back to the slug if
            // the topic is empty / non-Windows-safe.
            string batchProjectDisplayName = !string.IsNullOrWhiteSpace(rawTopic)
                ? rawTopic.Trim()
                : batchProjectName;

            // ── Idempotent BatchProject lookup ──
            // If a previous run already created this project, reuse its ProjectId +
            // FlowProjectId + Items so we don't orphan the Flow project on Google's side
            // and so the user can retry only the missing scenes from the Batch tab.
            BatchProjectModel? batchProject = null;
            try
            {
                batchProject = await _batchProjectService.GetOrCreateProjectAsync(batchProjectName);
                // Preserve the human-friendly display name on every run so the user
                // can locate the project in the dashboard by topic.
                if (!string.IsNullOrWhiteSpace(batchProjectDisplayName))
                {
                    batchProject.ProjectName = batchProjectDisplayName;
                }
                // ScriptJson stores the path to scenes.json (not its full content) so
                // project.json stays small (was bloating from ~5 KB to ~60 KB). The
                // Batch Image Gen dashboard reads scenes.json directly from disk when
                // it needs the full scene breakdown.
                batchProject.ScriptJson = scenesPath;
                batchProject.OutputDir = imgDir;
                batchProject.Provider = provider;
                batchProject.Engine = "flow";
                batchProject.Model = defaultModel;
                batchProject.AspectRatio = "16:9";
                batchProject.Concurrency = MaxConcurrentImages;
                if (!string.IsNullOrWhiteSpace(characterRefPath) && File.Exists(characterRefPath))
                {
                    batchProject.RefImagePaths = new List<string> { characterRefPath };
                }
                // Persist the freshly populated BatchProject (ref image path, scenes.json
                // pointer, output dir, etc.) BEFORE we do any HTTP work. This guarantees
                // a future re-run — even if every subsequent Flow call fails — still sees
                // RefImagePaths on disk instead of an empty list, which would force the
                // Batch Image Gen dashboard to drop the character reference on regeneration.
                try { await _batchProjectService.SaveProjectAsync(batchProject); }
                catch (Exception saveEx)
                {
                    logTask(task, $"[STEP 5] ⚠️ Could not persist BatchProject metadata: {saveEx.Message}");
                }
                logTask(task, $"[STEP 5] 📂 Using BatchProject '{batchProject.ProjectName}' (id={batchProject.ProjectId}).");
            }
            catch (Exception projInitEx)
            {
                // Don't kill the pipeline just because the dashboard couldn't be created.
                logTask(task, $"[STEP 5] ⚠️ Could not initialise BatchProject: {projInitEx.Message}");
            }

            // Reuse the Flow project id/url captured by the previous run so we keep
            // appending to the same Flow project (which is also what the user sees in
            // the Batch tab and on https://labs.google/fx/tools/flow/).
            string? flowProjectId = batchProject?.FlowProjectId;
            string? flowProjectUrl = batchProject?.FlowProjectUrl;
            // Flow uses the slug as its project title — it must be filename-safe.
            string flowProjectTitle = batchProjectName;
            // Kept for the projectTitle argument below; this is the value forwarded
            // to BatchImageItem.FlowProjectTitle (and used as the fallback Flow
            // project title if CreateProjectAsync fails).
            string projectTitle = batchProjectName;

            // ── Step 1: Create Flow project FIRST (only if we don't already have one) ──
            // This guarantees the (project_id, sha256) cache on Flow server returns the
            // same media_id, and that the project's media library actually contains the
            // reference image used.
            if (string.IsNullOrEmpty(flowProjectId))
            {
                string? flowError = null;
                (flowProjectId, flowProjectUrl, flowError) = await FlowLocalImageGenProvider.CreateProjectAsync(serverUrl, apiKey, flowProjectTitle);

                if (!string.IsNullOrEmpty(flowError) || string.IsNullOrEmpty(flowProjectId))
                {
                    logTask(task, $"[STEP 5] [WARNING] Could not create Flow project upfront ('{flowError ?? "no project_id"}'). Will fall back to provider-side default project at generation time.");
                }
                else
                {
                    logTask(task, $"[STEP 5] ✅ Created Flow Project '{flowProjectTitle}' (id={flowProjectId}, url={flowProjectUrl ?? "n/a"}).");
                }

                // Persist the freshly created FlowProjectId back into the BatchProject
                // BEFORE we send any generation requests. If the Flow HTTP call works
                // but disk save fails, we still want a future re-run to reuse the same
                // Flow project rather than spamming Google with new ones.
                if (batchProject != null && !string.IsNullOrEmpty(flowProjectId))
                {
                    batchProject.FlowProjectId = flowProjectId;
                    batchProject.FlowProjectUrl = flowProjectUrl;
                    try { await _batchProjectService.SaveProjectAsync(batchProject); }
                    catch (Exception saveEx)
                    {
                        logTask(task, $"[STEP 5] ⚠️ Could not persist FlowProjectId into BatchProject: {saveEx.Message}");
                    }
                }
            }
            else
            {
                logTask(task, $"[STEP 5] ♻️ Reusing existing Flow Project id={flowProjectId} from BatchProject.");
            }

            // Reset the in-memory reference media cache so the first item uploads + caches cleanly
            // for *this* Flow project. This avoids cross-project stale IDs.
            FlowLocalImageGenProvider.ClearReferenceMediaCache();

            // Build items list from scenes. FlowProjectId is assigned now so the provider
            // will send project_id on every upload + generation request.
            //
            // Skip logic: if a previous run already wrote a canonical file for a scene
            // AND its status is Done, treat it as a no-op so re-running the pipeline only
            // touches the scenes that actually failed (HTTP 402, 429, etc.).
            var items = new List<(BatchImageItem item, string sceneId, int index, int sceneNumber, bool skip)>();
            int preExistingDoneCount = 0;

            // Lookup table: existing BatchImageItemState keyed by scene.scene so we can
            // pull across media_id / reference_media_id if those were captured previously.
            Dictionary<int, BatchImageItemState> existingBySceneNumber =
                (batchProject?.Items ?? new List<BatchImageItemState>())
                .Where(i => i.Index > 0)
                .GroupBy(i => i.Index)
                .ToDictionary(g => g.Key, g => g.First());

            for (int i = 0; i < rootData.scenes.Count; i++)
            {
                var scene = rootData.scenes[i];
                string sceneId = string.IsNullOrWhiteSpace(scene.id) ? $"scene_{scene.scene:D3}" : scene.id;
                int sceneNumber = scene.scene > 0 ? scene.scene : (i + 1);

                string canonicalPath = Path.Combine(imgDir, $"{sceneId}.png");
                bool fileExists = File.Exists(canonicalPath);

                BatchImageItemState? existing = null;
                existingBySceneNumber.TryGetValue(sceneNumber, out existing);

                bool wasDonePreviously = existing != null
                    && string.Equals(existing.Status, "Done", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(existing.ImagePath)
                    && File.Exists(existing.ImagePath);

                // Prefer canonical file on disk; fall back to whatever path was saved.
                bool skip = (fileExists || wasDonePreviously);

                if (skip)
                {
                    preExistingDoneCount++;
                }

                var batchItem = new BatchImageItem
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
                    Status = skip ? "Done" : "Processing",
                    // Carry the cached media_id forward so re-runs do NOT need to re-upload
                    // the reference image, and the Batch tab can drive the same Flow project.
                    MediaId = existing?.MediaId,
                    ReferenceMediaId = existing?.ReferenceMediaId,
                };

                if (skip)
                {
                    if (fileExists)
                    {
                        batchItem.ImagePath = canonicalPath;
                    }
                    else if (existing != null && !string.IsNullOrEmpty(existing.ImagePath))
                    {
                        batchItem.ImagePath = existing.ImagePath;
                    }
                }

                items.Add((batchItem, sceneId, i, sceneNumber, skip));
            }

            if (preExistingDoneCount > 0)
            {
                logTask(task, $"[STEP 5] ⏭️ {preExistingDoneCount}/{items.Count} scenes already have valid images on disk — skipping regeneration. Use Batch Image Gen tab to retry the rest.");
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

                    if (entry.skip)
                    {
                        logTask(task, $"[STEP 5] ⏭️ Skipping Image {current}/{total} ({entry.sceneId}) — already on disk.");
                        return;
                    }

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

            // ── Always sync BatchProject items + flow project id back to disk ──
            // Even when 100% of items failed (HTTP 402 across the board), the user
            // still needs the BatchProject to exist so they can retry from the
            // Batch Image Gen tab once the Flow session has been refreshed.
            await SyncBatchProjectItemsAsync(
                task, batchProject, items, imgDir,
                provider, defaultModel,
                flowProjectId, flowProjectUrl, projectTitle,
                batchProjectDisplayName,
                rootData, characterRefPath,
                logTask);

            int generatedCount = successCount;
            int alreadyDone = items.Count(i => i.skip);
            if (alreadyDone > 0)
            {
                task.Step5Status = generatedCount > 0 || alreadyDone == items.Count ? "Done" : "Failed";
            }
            else
            {
                task.Step5Status = generatedCount == items.Count ? "Done" : "Failed";
            }
            logTask(task, $"[STEP 5] Finished. Generated {generatedCount}, skipped {alreadyDone}, failed {failCount} of {items.Count} scenes.");
        }

        /// <summary>
        /// Merges the freshly generated <paramref name="items"/> state back into
        /// the persistent <paramref name="batchProject"/> (creating a fresh
        /// project if the early-init lookup failed) and saves it. This is the
        /// single source of truth that powers the Batch Image Gen tab.
        /// </summary>
        private async Task SyncBatchProjectItemsAsync(
            AutomationTask task,
            BatchProjectModel? batchProject,
            List<(BatchImageItem item, string sceneId, int index, int sceneNumber, bool skip)> items,
            string imgDir,
            string provider,
            string defaultModel,
            string? flowProjectId,
            string? flowProjectUrl,
            string projectTitle,
            string? batchProjectDisplayName,
            ScenesJsonRootModel? rootData,
            string? characterRefPath,
            Action<AutomationTask, string> logTask)
        {
            try
            {
                if (batchProject == null)
                {
                    // GetOrCreate failed earlier (e.g.ProjectsStorageDir not writable).
                    // Re-try once here so we still get a usable dashboard entry.
                    string fallbackName = !string.IsNullOrWhiteSpace(projectTitle) ? projectTitle : "Pipeline_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    batchProject = await _batchProjectService.GetOrCreateProjectAsync(fallbackName);
                    if (!string.IsNullOrWhiteSpace(batchProjectDisplayName))
                    {
                        batchProject.ProjectName = batchProjectDisplayName;
                    }
                    // Store scenes.json path (not its full content) — see comment in
                    // the main init block above.
                    string fallbackScenesPath = rootData != null
                        ? Path.Combine(imgDir, "scenes.json")
                        : string.Empty;
                    batchProject.ScriptJson = fallbackScenesPath;
                    batchProject.OutputDir = imgDir;
                    batchProject.Provider = provider;
                    batchProject.Engine = "flow";
                    batchProject.Model = defaultModel;
                    batchProject.AspectRatio = "16:9";
                    batchProject.Concurrency = MaxConcurrentImages;
                    if (!string.IsNullOrWhiteSpace(characterRefPath) && File.Exists(characterRefPath))
                    {
                        batchProject.RefImagePaths = new List<string> { characterRefPath };
                    }
                }
                else if (!string.IsNullOrWhiteSpace(batchProjectDisplayName))
                {
                    // Keep the topic as the user-facing name across re-runs.
                    batchProject.ProjectName = batchProjectDisplayName;
                    // Persist scenes.json path every re-run so the dashboard always
                    // points at the latest breakdown (in case the user rerun the
                    // pipeline with a different topic).
                    batchProject.ScriptJson = rootData != null
                        ? Path.Combine(imgDir, "scenes.json")
                        : batchProject.ScriptJson;
                }

                if (!string.IsNullOrEmpty(flowProjectId))
                {
                    batchProject.FlowProjectId = flowProjectId;
                    batchProject.FlowProjectUrl = flowProjectUrl;
                }

                // Build a lookup from the on-disk state so we can carry forward
                // media_id / reference_media_id for items that didn't run this pass.
                Dictionary<int, BatchImageItemState> existingBySceneNumber =
                    (batchProject.Items ?? new List<BatchImageItemState>())
                    .Where(i => i.Index > 0)
                    .GroupBy(i => i.Index)
                    .ToDictionary(g => g.Key, g => g.First());

                var mergedItems = new List<BatchImageItemState>();
                foreach (var entry in items)
                {
                    string sceneId = entry.sceneId;

                    string canonicalPath = Path.Combine(imgDir, $"{sceneId}.png");
                    string? imagePath = null;

                    if (!string.IsNullOrEmpty(entry.item.ImagePath) && File.Exists(entry.item.ImagePath))
                    {
                        imagePath = entry.item.ImagePath;
                    }
                    else if (File.Exists(canonicalPath))
                    {
                        imagePath = canonicalPath;
                    }
                    else
                    {
                        // Provider may have written a flow_image_<Index>_<ts>.png fallback.
                        string indexed = $"_{entry.sceneNumber}_";
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
                        }
                    }

                    bool isDone = string.Equals(entry.item.Status, "Done", StringComparison.OrdinalIgnoreCase)
                                  && !string.IsNullOrEmpty(imagePath);

                    existingBySceneNumber.TryGetValue(entry.sceneNumber, out var existing);

                    mergedItems.Add(new BatchImageItemState
                    {
                        Index = entry.sceneNumber,
                        SceneTitle = entry.item.SceneTitle,
                        Transcript = entry.item.Transcript,
                        Prompt = entry.item.Prompt,
                        Status = isDone ? "Done"
                               : (existing?.Status ?? (entry.skip ? "Done" : "Failed")),
                        ImagePath = isDone ? imagePath! : (existing?.ImagePath ?? string.Empty),
                        ErrorMessage = isDone
                            ? string.Empty
                            : (entry.item.ErrorMessage ?? existing?.ErrorMessage ?? "Image file not found after generation"),
                        MediaId = !string.IsNullOrEmpty(entry.item.MediaId) ? entry.item.MediaId : existing?.MediaId,
                        ReferenceMediaId = !string.IsNullOrEmpty(entry.item.ReferenceMediaId) ? entry.item.ReferenceMediaId : existing?.ReferenceMediaId,
                        FlowProjectId = !string.IsNullOrEmpty(flowProjectId) ? flowProjectId : existing?.FlowProjectId,
                        FlowProjectTitle = string.IsNullOrEmpty(flowProjectId) ? projectTitle : null,
                        FlowProjectUrl = !string.IsNullOrEmpty(flowProjectUrl) ? flowProjectUrl : existing?.FlowProjectUrl,
                        Engine = "flow",
                        Model = defaultModel,
                        AspectRatio = "16:9",
                        Upscale = "none"
                    });
                }

                batchProject.Items = mergedItems;
                await _batchProjectService.SaveProjectAsync(batchProject);

                int done = mergedItems.Count(i => string.Equals(i.Status, "Done", StringComparison.OrdinalIgnoreCase));
                int failed = mergedItems.Count - done;
                logTask(task, $"[STEP 5] 💾 BatchProject '{batchProject.ProjectName}' synced: {done} done, {failed} pending. Open Batch Image Gen tab to retry the failed scenes.");

                // Fire the in-process event so the Batch Image Gen dashboard can
                // reload its project list immediately, even if the user is looking
                // at the Gemini tab when this run finishes. Subscribers must be cheap.
                PipelineEvents.RaiseBatchProjectUpdated(batchProject.ProjectName);
            }
            catch (Exception ex)
            {
                logTask(task, $"[STEP 5] ⚠️ Failed to sync BatchProject: {ex.Message}");
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
