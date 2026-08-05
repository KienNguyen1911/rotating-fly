using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Application.Services.Providers;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service responsible for Batch Image Generation.
    /// Uses ImageGenProviderFactory to delegate execution to the appropriate Strategy Provider (G-Labs vs Flow Local).
    /// </summary>
    public class BatchImageGenService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Tests connection health to the target server. Accepts either the bare root URL
        /// ("http://127.0.0.1:8787") or the OpenAI-compatible base ("http://127.0.0.1:8787/v1").
        /// Important: server's /health is registered at root, so callers must NOT append
        /// "/health" to a URL that already ends with "/v1".
        /// </summary>
        public async Task<bool> TestHealthAsync(string serverUrl)
        {
            try
            {
                string url = (serverUrl ?? string.Empty).Trim().TrimEnd('/');
                // Strip a trailing /v1 so the health probe hits the root.
                if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                {
                    url = url.Substring(0, url.Length - 3).TrimEnd('/');
                }
                if (string.IsNullOrEmpty(url))
                {
                    url = "http://127.0.0.1:8787";
                }
                url += "/health";
                System.Diagnostics.Debug.WriteLine($"[BatchImageGen.TestHealthAsync] GET {url}");
                var response = await _httpClient.GetAsync(url);
                System.Diagnostics.Debug.WriteLine($"[BatchImageGen.TestHealthAsync] {url} -> {(int)response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BatchImageGen.TestHealthAsync] EXCEPTION: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generates a single image item by delegating to the Strategy Provider specified in item.Provider.
        /// </summary>
        public async Task ProcessSingleImageItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory)
        {
            var provider = ImageGenProviderFactory.GetProvider(item.Provider);
            await provider.ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImages, outputDirectory);
        }

        /// <summary>
        /// Generates a single image item with full file path details for reference images.
        /// </summary>
        public async Task ProcessSingleImageItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag, string filePath)> referenceImagesWithFilePath,
            string outputDirectory)
        {
            var provider = ImageGenProviderFactory.GetProvider(item.Provider);
            if (provider is FlowLocalImageGenProvider flowProvider)
            {
                await flowProvider.ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImagesWithFilePath, outputDirectory);
            }
            else
            {
                await provider.ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImagesWithFilePath.ConvertAll(r => (r.base64Data, r.tag)), outputDirectory);
            }
        }
    }
}