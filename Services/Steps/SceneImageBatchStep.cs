using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AssetAutomator
{
    /// <summary>
    /// Pipeline Step 5: Reads scenes.json, parses image_prompts for all scenes,
    /// and invokes BatchImageGenService to generate scene images concurrently (defaulting to flow_local provider).
    /// </summary>
    public class SceneImageBatchStep
    {
        private readonly BatchImageGenService _batchImageGenService;

        public SceneImageBatchStep(BatchImageGenService batchImageGenService)
        {
            _batchImageGenService = batchImageGenService;
        }

        public async Task ExecuteAsync(
            string outputDir,
            string? providerKey,
            AutomationTask task,
            Action<AutomationTask, string> logTask)
        {
            logTask(task, "[STEP 5] Starting Batch Image Generation for Scenes...");
            task.Step5Status = "In Progress";

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

            string provider = string.IsNullOrWhiteSpace(providerKey) ? (ConfigService.CurrentSettings.DefaultImageGenProvider ?? "flow_local") : providerKey;
            string serverUrl = ConfigService.CurrentSettings.ImageApiUrl;
            string apiKey = ConfigService.CurrentSettings.ImageApiKey;

            logTask(task, $"[STEP 5] Found {rootData.scenes.Count} scenes. Using Provider: '{provider}'...");

            int successCount = 0;
            var referenceImages = new List<(string base64Data, string tag, string filePath)>();

            for (int i = 0; i < rootData.scenes.Count; i++)
            {
                var scene = rootData.scenes[i];
                string sceneId = string.IsNullOrWhiteSpace(scene.id) ? $"scene_{scene.scene:D3}" : scene.id;

                var item = new BatchImageItem
                {
                    Index = scene.scene > 0 ? scene.scene : (i + 1),
                    TaskId = task.Id.ToString(),
                    Prompt = scene.image_prompt,
                    Transcript = scene.transcript,
                    SceneTitle = $"Scene #{scene.scene}: {sceneId}",
                    Provider = provider,
                    Status = "Processing"
                };

                try
                {
                    logTask(task, $"[STEP 5] Generating Image {i + 1}/{rootData.scenes.Count} ({sceneId})...");
                    await _batchImageGenService.ProcessSingleImageItemAsync(
                        item,
                        serverUrl,
                        apiKey,
                        referenceImages,
                        outputDir
                    );

                    if (item.Status.Equals("Done", StringComparison.OrdinalIgnoreCase))
                    {
                        successCount++;
                    }
                    else
                    {
                        logTask(task, $"[STEP 5] [WARNING] Scene {sceneId} status: {item.Status}. Error: {item.ErrorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    logTask(task, $"[STEP 5] [ERROR] Exception generating image for {sceneId}: {ex.Message}");
                }
            }

            task.Step5Status = "Completed";
            logTask(task, $"[STEP 5] Success! Finished Batch Image Generation. ({successCount}/{rootData.scenes.Count} images created).");
        }
    }
}
