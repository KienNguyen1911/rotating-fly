# P4.2 — Centralize Magic Numbers into TimingConstants

## Goal
Extract all hardcoded timeout and delay values scattered throughout the codebase into a centralized `TimingConstants.cs` file for easier maintenance and tuning.

## Why
- Magic numbers (like `15000`, `5000`, `2000`) make code harder to read and tune.
- Changing a timeout required finding and updating every occurrence.
- Centralized constants enable runtime configuration in the future.

## New File: `Core/TimingConstants.cs`

Created a new static class with three nested static classes:

### `Timeouts`
| Constant | Value | Purpose |
|----------|-------|---------|
| `DefaultHttpTimeoutMs` | 30,000 | Default HTTP request timeout |
| `ExtendedHttpTimeoutMs` | 600,000 | Long-running operations (10 min) |
| `ShortHttpTimeoutMs` | 5,000 | Quick health checks |
| `MediumHttpTimeoutMs` | 15,000 | Moderate operations |
| `PageNavigationTimeoutMs` | 30,000 | Playwright page navigation |
| `ElementWaitTimeoutMs` | 10,000 | Waiting for DOM elements |
| `ShortElementWaitMs` | 3,000 | Already-visible elements |
| `StatusPollIntervalMs` | 5,000 | Server status polling |
| `BrowserCloseTimeoutMs` | 15,000 | Browser close operation |
| `PythonServerTimeoutMs` | 3,000 | Python server HTTP calls |

### `Delays`
| Constant | Value | Purpose |
|----------|-------|---------|
| `RetryDelayMs` | 1,000 | Retry before operation |
| `RewriteRetryDelayMs` | 4,000 | ChatGPT rewrite retries |
| `AiResponseDelayMs` | 5,000 | AI service response wait |
| `PollIntervalMs` | 500 | General polling |
| `FastPollIntervalMs` | 300 | Fast polling |
| `SlowPollIntervalMs` | 1,000 | Slow polling |
| `UiSettleDelayMs` | 1,000 | Let UI settle |
| `PageRenderDelayMs` | 2,000 | Page render wait |
| `NetworkIdleDelayMs` | 3,000 | Network idle wait |
| `AiWarmupDelayMs` | 1,500 | AI warmup delay |
| `HumanTypingMinMs` | 20 | Min typing delay |
| `HumanTypingMaxMs` | 70 | Max typing delay |
| `HumanActionMinMs` | 50 | Min action delay |
| `HumanActionMaxMs` | 150 | Max action delay |
| `HumanThinkMinMs` | 500 | Min think delay |
| `HumanThinkMaxMs` | 800 | Max think delay |
| `PythonServerWarmupMs` | 300 | Python server init |
| `ImageBatchDelayMs` | 3,000 | Image batch processing |
| `VoiceoverDelayMs` | 2,000 | Voiceover processing |
| `VideoCooldownSeconds` | 5 | Video cooldown |

### `RetryConfig`
| Constant | Value | Purpose |
|----------|-------|---------|
| `MaxRetries` | 3 | Max retry attempts |
| `MaxAiRetries` | 5 | AI service retries |
| `MaxLongPollWaitMinutes` | 10 | Long polling max wait |

### `ServiceLimits`
| Constant | Value | Purpose |
|----------|-------|---------|
| `MaxConcurrentTasks` | 3 | Max concurrent tasks |
| `MaxConcurrentImageRequests` | 5 | Max image gen requests |
| `BatchSize` | 10 | Bulk operation batch size |
| `MaxFileSizeBytes` | 50 MB | Max upload size |
| `BatchProgressIntervalSeconds` | 5 | Progress update interval |

## Files Updated

1. **GeminiPlaywrightSceneCreator.cs**
   - Removed local `SELECTOR_TIMEOUT` constant (now uses `Timeouts.PageNavigationTimeoutMs`)
   - Removed local `COOLDOWN_SECONDS` constant (now uses `Delays.VideoCooldownSeconds`)
   - Updated page navigation timeout to use `Timeouts.PageNavigationTimeoutMs`
   - Updated render delay to use `Delays.PageRenderDelayMs`

2. **BrowserService.cs**
   - Updated browser close timeout to use `Timeouts.BrowserCloseTimeoutMs`

3. **ChatGptRewriteStep.cs**
   - Updated all rewrite retry delays to use `Delays.RewriteRetryDelayMs`
   - Updated AI warmup delay to use `Delays.AiWarmupDelayMs`
   - Updated page render delay to use `Delays.PageRenderDelayMs`
   - Updated AI response delay to use `Delays.AiResponseDelayMs`

4. **HumanBehaviourHelper.cs**
   - Updated typing delays to use `Delays.HumanTypingMinMs` / `Delays.HumanTypingMaxMs`
   - Updated action delays to use `Delays.HumanActionMinMs` / `Delays.HumanActionMaxMs`
   - Reused single `Random` instance for better performance

5. **ImageGenerationStep.cs**
   - Updated image batch polling delay to use `Delays.ImageBatchDelayMs`

6. **VoiceoverGenerationStep.cs**
   - Updated voiceover polling delay to use `Delays.VoiceoverDelayMs`
   - Updated transcript polling delay to use `Delays.PageRenderDelayMs`

## Validation
- `dotnet build` succeeds with 0 errors
- All magic numbers replaced with named constants
- Constants are grouped by category (Timeouts, Delays, RetryConfig, ServiceLimits)

## Future Enhancements
- Add `IOptions<T>` pattern to make these configurable via `appsettings.json`
- Add environment variable overrides for production tuning
- Add telemetry to track actual wait times vs. configured values
