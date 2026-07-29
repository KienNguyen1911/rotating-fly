namespace AssetAutomator.Core
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

        /// <summary>Wait for browser close operation.</summary>
        public const int BrowserCloseTimeoutMs = 15_000;

        /// <summary>Timeout for Python server management HTTP calls.</summary>
        public const int PythonServerTimeoutMs = 3_000;
    }

    /// <summary>
    /// Centralized delay constants for retry logic, polling, and human behavior simulation.
    /// All delay values are in milliseconds.
    /// </summary>
    public static class Delays
    {
        // === Retry Delays ===
        /// <summary>Delay before retrying a failed operation.</summary>
        public const int RetryDelayMs = 1_000;

        /// <summary>Extended delay before retrying after a longer failure.</summary>
        public const int RetryDelayExtendedMs = 2_000;

        /// <summary>Delay before retrying ChatGPT/Rewrite operations.</summary>
        public const int RewriteRetryDelayMs = 4_000;

        /// <summary>Delay after sending prompt to AI service.</summary>
        public const int AiResponseDelayMs = 5_000;

        /// <summary>Delay for poll interval when waiting for server responses.</summary>
        public const int PollIntervalMs = 500;

        /// <summary>Delay for faster polling when response is near.</summary>
        public const int FastPollIntervalMs = 300;

        /// <summary>Delay for slow polling when starting to wait.</summary>
        public const int SlowPollIntervalMs = 1_000;

        // === UI Settling Delays ===
        /// <summary>Delay to let UI settle after an action.</summary>
        public const int UiSettleDelayMs = 1_000;

        /// <summary>Delay to let page fully render after navigation.</summary>
        public const int PageRenderDelayMs = 2_000;

        /// <summary>Delay to wait for network to become idle.</summary>
        public const int NetworkIdleDelayMs = 3_000;

        /// <summary>Extended delay for complex page loads.</summary>
        public const int ComplexPageDelayMs = 3_000;

        /// <summary>Delay for AI service warmup.</summary>
        public const int AiWarmupDelayMs = 1_500;

        // === Human Behavior Simulation ===
        /// <summary>Minimum delay to simulate human typing.</summary>
        public const int HumanTypingMinMs = 20;

        /// <summary>Maximum delay to simulate human typing.</summary>
        public const int HumanTypingMaxMs = 70;

        /// <summary>Minimum delay between actions.</summary>
        public const int HumanActionMinMs = 50;

        /// <summary>Maximum delay between actions.</summary>
        public const int HumanActionMaxMs = 150;

        /// <summary>Minimum delay for human reading/thinking.</summary>
        public const int HumanThinkMinMs = 500;

        /// <summary>Maximum delay for human reading/thinking.</summary>
        public const int HumanThinkMaxMs = 800;

        // === Service Warmup ===
        /// <summary>Delay for Python server to initialize.</summary>
        public const int PythonServerWarmupMs = 300;

        /// <summary>Delay for image generation batch processing.</summary>
        public const int ImageBatchDelayMs = 3_000;

        /// <summary>Delay for voiceover processing.</summary>
        public const int VoiceoverDelayMs = 2_000;

        /// <summary>Delay between transcript extraction retries.</summary>
        public const int TranscriptRetryDelayMs = 3_000;

        /// <summary>Cooldown between video processing tasks.</summary>
        public const int VideoCooldownSeconds = 5;
    }

    /// <summary>
    /// Centralized retry configuration constants.
    /// </summary>
    public static class RetryConfig
    {
        /// <summary>Maximum number of retry attempts for transient failures.</summary>
        public const int MaxRetries = 3;

        /// <summary>Maximum number of retry attempts for AI service calls.</summary>
        public const int MaxAiRetries = 5;

        /// <summary>Maximum wait time for long polling operations.</summary>
        public const int MaxLongPollWaitMinutes = 10;

        /// <summary>Base delay for exponential backoff (in milliseconds).</summary>
        public const int ExponentialBackoffBaseMs = 1_000;
    }

    /// <summary>
    /// Centralized configuration for external service endpoints and limits.
    /// </summary>
    public static class ServiceLimits
    {
        /// <summary>Maximum concurrent tasks allowed.</summary>
        public const int MaxConcurrentTasks = 3;

        /// <summary>Maximum concurrent image generation requests.</summary>
        public const int MaxConcurrentImageRequests = 5;

        /// <summary>Batch size for bulk operations.</summary>
        public const int BatchSize = 10;

        /// <summary>Maximum file size for upload (in bytes).</summary>
        public const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

        /// <summary>Default poll interval for batch progress updates (seconds).</summary>
        public const int BatchProgressIntervalSeconds = 5;
    }
}
