using AssetAutomator.Core.Interfaces;

namespace AssetAutomator.Application.Services.Providers
{
    /// <summary>
    /// Factory for retrieving the singleton <see cref="IImageGenProvider"/> implementation.
    /// The application now ships a single image-gen strategy
    /// (<c>FlowLocalImageGenProvider</c>) backed by the Google Flow Local API
    /// running on <c>D:\Dev\google-flow-2.0.0</c>.
    /// </summary>
    public static class ImageGenProviderFactory
    {
        public const string FlowLocalProviderKey = "flow_local";

        private static readonly FlowLocalImageGenProvider _flowLocal = new FlowLocalImageGenProvider();

        /// <summary>
        /// Returns the configured provider. Any unknown / legacy key (e.g. <c>"glabs"</c>
        /// saved by older projects) is gracefully remapped to <c>flow_local</c>.
        /// </summary>
        public static IImageGenProvider GetProvider(string? providerKey)
        {
            if (string.Equals(providerKey, FlowLocalProviderKey, System.StringComparison.OrdinalIgnoreCase))
            {
                return _flowLocal;
            }

            return _flowLocal;
        }
    }
}