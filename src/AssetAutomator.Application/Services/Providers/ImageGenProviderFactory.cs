using System;
using System.Collections.Generic;

namespace AssetAutomator.Application.Services.Providers
{
    /// <summary>
    /// Factory for creating and retrieving IImageGenProvider strategies based on provider key.
    /// Supports "glabs" and "flow_local".
    /// </summary>
    public static class ImageGenProviderFactory
    {
        private static readonly Dictionary<string, IImageGenProvider> _providers = new Dictionary<string, IImageGenProvider>(StringComparer.OrdinalIgnoreCase)
        {
            ["glabs"] = new GlabsImageGenProvider(),
            ["flow_local"] = new FlowLocalImageGenProvider()
        };

        /// <summary>
        /// Retrieves the requested IImageGenProvider instance.
        /// Defaults to GlabsImageGenProvider if key is null or unknown.
        /// </summary>
        public static IImageGenProvider GetProvider(string providerKey)
        {
            if (!string.IsNullOrWhiteSpace(providerKey) && _providers.TryGetValue(providerKey.Trim(), out var provider))
            {
                return provider;
            }

            return _providers["glabs"];
        }
    }
}