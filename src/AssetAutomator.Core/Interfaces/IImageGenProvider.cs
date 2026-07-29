using System.Collections.Generic;
using System.Threading.Tasks;

namespace AssetAutomator.Core.Interfaces
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

    /// <summary>
    /// Model for batch image generation items.
    /// </summary>
    public class BatchImageItem
    {
        public string Id { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string? ReferenceImagePath { get; set; }
        public string? AspectRatio { get; set; }
        public int? Seed { get; set; }
    }
}
