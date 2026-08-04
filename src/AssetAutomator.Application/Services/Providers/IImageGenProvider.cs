using System.Collections.Generic;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Core.Interfaces
{
    /// <summary>
    /// Strategy interface for the Image Generation Provider.
    /// The application uses a single concrete strategy
    /// (<c>FlowLocalImageGenProvider</c>) backed by Google Flow Local API.
    /// </summary>
    public interface IImageGenProvider
    {
        /// <summary>
        /// Unique key identifying the provider. Always <c>"flow_local"</c>.
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// Executes a single image generation request using the Google Flow Local API.
        /// </summary>
        Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory);
    }
}