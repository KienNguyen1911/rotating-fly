using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetAutomator.Application.Services;
using AssetAutomator.Core.Interfaces;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Steps
{
    /// <summary>
    /// Pipeline Step 5: Reads scenes.json, parses image_prompts for all scenes,
    /// and invokes BatchImageGenService to generate scene images concurrently (defaulting to flow_local provider).
    /// Uses SemaphoreSlim to cap concurrent image generation at MaxConcurrentImages.
    /// </summary>
    public class SceneImageBatchStep
    {
        private readonly BatchImageGenService _batchImageGenService;
        private readonly IConfigService _configService;
        private const int MaxConcurrentImages = 6;

        public SceneImageBatchStep(BatchImageGenService batchImageGenService, IConfigService configService)
        {
            _batchImageGenService = batchImageGenService;
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

            int successCount = 0;
            int failCount = 0;
            var referenceImages = new List<(string base64Data, string tag, string filePath)>();

            if (!string.IsNullOrWhiteSpace(task.CharacterRef) && File.Exists(task.CharacterRef))
            {
                try
                {
                    byte[] imageBytes = await File.ReadAllBytesAsync(task.CharacterRef);
                    string base64 = Convert.ToBase64String(imageBytes);
                    referenceImages.Add((base64, "@character", task.CharacterRef));
                    logTask(task, $"[STEP 5] Loaded Character Reference Image: '{Path.GetFileName(task.CharacterRef)}'");
                }
                catch (Exception ex)
                {
                    logTask(task, $"[STEP 5] [WARNING] Failed to read Character Reference file: {ex.Message}");
                }
            }

            // Build items list from scenes
            var items = new List<(BatchImageItem item, string sceneId, int index)>();
            for (int i = 0; i < rootData.scenes.Count; i++)
            {
                var scene = rootData.scenes[i];
                string sceneId = string.IsNullOrWhiteSpace(scene.id) ? $"scene_{scene.scene:D3}" : scene.id;

                items.Add((new BatchImageItem
                {
                    Index = scene.scene > 0 ? scene.scene : (i + 1),
                    TaskId = task.Id.ToString(),
                    Prompt = scene.image_prompt,
                    Transcript = scene.transcript,
                    SceneTitle = $"Scene #{scene.scene}: {sceneId}",
                    Provider = provider,
                    Engine = "flow",
                    Model = defaultModel,
                    AspectRatio = "16:9",
                    Status = "Processing"
                }, sceneId, i));
            }

            // Concurrent generation with semaphore-based throttling
            using var semaphore = new SemaphoreSlim(MaxConcurrentImages);
            int total = items.Count;
            int processed = 0;

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
        }
    }
}