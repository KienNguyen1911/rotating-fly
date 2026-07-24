using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AssetAutomator.Services.Providers;

namespace AssetAutomator
{
    /// <summary>
    /// Service responsible for Batch Image Generation.
    /// Uses ImageGenProviderFactory to delegate execution to the appropriate Strategy Provider (G-Labs vs Flow Local).
    /// </summary>
    public class BatchImageGenService
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Tests connection health to the target server.
        /// </summary>
        public async Task<bool> TestHealthAsync(string serverUrl)
        {
            try
            {
                string url = serverUrl.TrimEnd('/');
                if (!url.EndsWith("/health", StringComparison.OrdinalIgnoreCase))
                {
                    url += "/health";
                }
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
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
