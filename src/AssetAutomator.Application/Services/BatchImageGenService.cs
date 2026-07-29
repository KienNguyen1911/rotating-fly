using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AssetAutomator.Application.Services
{
    /// <summary>
    /// Service responsible for Batch Image Generation.
    /// Uses ImageGenProviderFactory to delegate execution to the appropriate Strategy Provider.
    /// </summary>
    public class BatchImageGenService
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

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

        public async Task ProcessSingleImageItemAsync(
            Core.Models.BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory)
        {
            var provider = Providers.ImageGenProviderFactory.GetProvider(item.Provider);
            await provider.ProcessSingleItemAsync(item, serverUrl, apiKey, referenceImages, outputDirectory);
        }

        public async Task ProcessSingleImageItemAsync(
            Core.Models.BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag, string filePath)> referenceImagesWithFilePath,
            string outputDirectory)
        {
            var provider = Providers.ImageGenProviderFactory.GetProvider(item.Provider);
            if (provider is Providers.FlowLocalImageGenProvider flowProvider)
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
