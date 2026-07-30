using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace AssetAutomator.Core.Interfaces
{
    /// <summary>
    /// Abstraction for browser lifecycle management: initialization, slot allocation,
    /// profile cloning, and cleanup. Implementations manage Playwright browser contexts.
    /// </summary>
    public interface IBrowserService
    {
        /// <summary>
        /// Active browser contexts keyed by profile path.
        /// </summary>
        ConcurrentDictionary<string, IBrowserContext> BrowserContexts { get; }

        /// <summary>
        /// Copies a Chrome profile directory, skipping cache folders to reduce size.
        /// </summary>
        void CopyProfileDirectory(string sourceDir, string destinationDir);

        /// <summary>
        /// Lightweight copy of only the essential files needed for cookie extraction from a Chrome profile.
        /// </summary>
        void CopyMinimalProfileForCookies(string sourceDir, string destinationDir);

        /// <summary>
        /// Initializes or reuses a browser context for the given profile path.
        /// Allocates a screen slot for window positioning and injects anti-detection scripts.
        /// </summary>
        Task<IBrowserContext> EnsureBrowserInitializedAsync(string profilePath);

        /// <summary>
        /// Safely closes a browser context with a timeout to prevent hanging.
        /// </summary>
        Task CloseBrowserSafelyAsync(IBrowserContext? context, string profilePath, Action<string>? taskLog = null);

        /// <summary>
        /// Cleans up a temporary profile directory, suppressing errors.
        /// </summary>
        void CleanupTempProfile(string tempProfilePath, Action<string>? taskLog = null);
    }
}
