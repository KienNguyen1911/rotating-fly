using System.Collections.Generic;
using System.Threading.Tasks;
using AssetAutomator.Core.Models;

namespace AssetAutomator.Application.Services.Providers
{
    /// <summary>
    /// Strategy interface for Image Generation Providers (e.g. G-Labs, Flow Local).
    /// Enforces decoupled implementation for each API backend.
    /// </summary>
    public interface IImageGenProvider
    {
        /// <summary>
        /// Unique key identifying the provider (e.g. "glabs", "flow_local").
        /// </summary>
        string ProviderKey { get; }

        /// <summary>
        /// Executes single image generation request using the provider's API logic.
        /// </summary>
        Task ProcessSingleItemAsync(
            BatchImageItem item,
            string serverUrl,
            string apiKey,
            List<(string base64Data, string tag)> referenceImages,
            string outputDirectory);
    }
}