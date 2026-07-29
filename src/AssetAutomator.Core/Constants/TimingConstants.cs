namespace AssetAutomator.Core.Constants
{
    /// <summary>
    /// Centralized timeout constants for HTTP clients and Playwright operations.
    /// All timeout values are in milliseconds unless specified.
    /// </summary>
    public static class Timeouts
    {
        /// <summary>Default HTTP request timeout for most API calls.</summary>
        public const int DefaultHttpTimeoutMs = 30_000;

        /// <summary>Extended timeout for long-running operations (e.g., Gemini response generation).</summary>
        public const int ExtendedHttpTimeoutMs = 600_000; // 10 minutes

        /// <summary>Short timeout for quick health checks and status checks.</summary>
        public const int ShortHttpTimeoutMs = 5_000;

        /// <summary>Medium timeout for moderate operations.</summary>
        public const int MediumHttpTimeoutMs = 15_000;

        /// <summary>Timeout for Playwright page navigation.</summary>
        public const int PageNavigationTimeoutMs = 30_000;

        /// <summary>Timeout for waiting for DOM elements to appear.</summary>
        public const int ElementWaitTimeoutMs = 10_000;

        /// <summary>Short timeout for waiting for already-visible elements.</summary>
        public const int ShortElementWaitMs = 3_000;

        /// <summary>Poll interval for checking server status.</summary>
        public const int StatusPollIntervalMs = 5_000;

        /// <summary>Timeout for browser close operation.</summary>
        public const int BrowserCloseTimeoutMs = 15_000;

        /// <summary>Short timeout for Python server HTTP calls.</summary>
        public const int PythonServerTimeoutMs = 3_000;
    }

    /// <summary>
    /// Centralized delay constants for polling, retries, and human-like behavior simulation.
    /// All delay values are in milliseconds unless specified.
    /// </summary>
    public static class Delays
    {
        /// <summary>Retry before operation delay.</summary>
        public const int RetryDelayMs = 1_000;

        /// <summary>ChatGPT rewrite retry delay.</summary>
        public const int RewriteRetryDelayMs = 4_000;

        /// <summary>AI service response wait delay.</summary>
        public const int AiResponseDelayMs = 5_000;

        /// <summary>General polling interval.</summary>
        public const int PollIntervalMs = 500;

        /// <summary>Fast polling interval.</summary>
        public const int FastPollIntervalMs = 300;

        /// <summary>Slow polling interval.</summary>
        public const int SlowPollIntervalMs = 1_000;

        /// <summary>Let UI settle delay.</summary>
        public const int UiSettleDelayMs = 1_000;

        /// <summary>Page render wait delay.</summary>
        public const int PageRenderDelayMs = 2_000;

        /// <summary>Network idle wait delay.</summary>
        public const int NetworkIdleDelayMs = 3_000;

        /// <summary>AI warmup delay.</summary>
        public const int AiWarmupDelayMs = 1_500;

        /// <summary>Minimum human typing delay (ms per keystroke).</summary>
        public const int HumanTypingMinMs = 20;

        /// <summary>Maximum human typing delay (ms per keystroke).</summary>
        public const int HumanTypingMaxMs = 70;

        /// <summary>Minimum human action delay.</summary>
        public const int HumanActionMinMs = 50;

        /// <summary>Maximum human action delay.</summary>
        public const int HumanActionMaxMs = 150;

        /// <summary>Minimum human think delay.</summary>
        public const int HumanThinkMinMs = 500;

        /// <summary>Maximum human think delay.</summary>
        public const int HumanThinkMaxMs = 800;

        /// <summary>Python server warmup delay.</summary>
        public const int PythonServerWarmupMs = 300;

        /// <summary>Image batch processing delay.</summary>
        public const int ImageBatchDelayMs = 3_000;

        /// <summary>Voiceover processing delay.</summary>
        public const int VoiceoverDelayMs = 2_000;

        /// <summary>Video cooldown seconds.</summary>
        public const int VideoCooldownSeconds = 5;
    }

    /// <summary>
    /// Retry configuration constants.
    /// </summary>
    public static class RetryConfig
    {
        /// <summary>Maximum retry attempts for general operations.</summary>
        public const int MaxRetries = 3;

        /// <summary>Maximum retry attempts for AI service calls.</summary>
        public const int MaxAiRetries = 5;

        /// <summary>Maximum long polling wait time in minutes.</summary>
        public const int MaxLongPollWaitMinutes = 10;
    }

    /// <summary>
    /// Service limit constants for concurrency and resource management.
    /// </summary>
    public static class ServiceLimits
    {
        /// <summary>Maximum concurrent tasks.</summary>
        public const int MaxConcurrentTasks = 3;

        /// <summary>Maximum concurrent image generation requests.</summary>
        public const int MaxConcurrentImageRequests = 5;

        /// <summary>Bulk operation batch size.</summary>
        public const int BatchSize = 10;

        /// <summary>Maximum file upload size in bytes (50 MB).</summary>
        public const int MaxFileSizeBytes = 52_428_800;

        /// <summary>Progress update interval in seconds.</summary>
        public const int BatchProgressIntervalSeconds = 5;
    }
}
