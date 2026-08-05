using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetAutomator.Infrastructure.Http
{
    /// <summary>
    /// Centralized Polly v8 resilience pipelines for named <see cref="HttpClient"/> instances.
    /// Keeps retry / circuit-breaker / timeout policies consistent across the app and out of
    /// individual call sites.
    ///
    /// Two flavors are exposed:
    ///   • <see cref="ConfigureStandardPipeline"/>  — for ordinary GET/POST calls (lookup,
    ///                                                  submit, download). Aggressive retry +
    ///                                                  circuit-breaker + per-attempt timeout.
    ///   • <see cref="ConfigureLongPollingPipeline"/>— for server-side polling calls where the
    ///                                                  server is already responding, just slowly.
    ///                                                  No circuit breaker; longer per-attempt
    ///                                                  timeout.
    /// </summary>
    public static class ResiliencePipelineDefaults
    {
        // ShouldHandle predicate used by both pipelines: retry on transport errors and on
        // 5xx/408/429 from upstream. 4xx other than 408/429 are client errors and shouldn't
        // be retried blindly (they just delay surfacing the real problem).
        private static readonly PredicateBuilder<HttpResponseMessage> TransientHttpFailure =
            new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(ex => ex is not OperationCanceledException || ex.CancellationToken == default)
                .Handle<TimeoutRejectedException>()
                .HandleResult(static response =>
                    (int)response.StatusCode >= 500
                    || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout       // 408
                    || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests);    // 429

        /// <summary>
        /// Standard pipeline for typical HTTP calls: 3 retries with exponential backoff +
        /// jitter, a circuit breaker that opens after a burst of failures, and a 60s
        /// per-attempt timeout.
        ///
        /// Total worst-case ceiling: 3 attempts × 60s + 1s + 3s + 9s backoff ≈ 3min20s
        /// before the caller observes the exception. That replaces the unbounded 100s default
        /// which previously let one AI84 hiccup freeze the entire pipeline.
        /// </summary>
        public static void ConfigureStandardPipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder) =>
            builder
                // Per-attempt timeout — kills requests that hang forever on a slow socket.
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(60),
                    OnTimeout = args =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ai84-resilience] Attempt timed out after {args.Timeout.TotalSeconds:F0}s.");
                        return ValueTask.CompletedTask;
                    }
                })
                // Retry with exponential backoff + jitter to avoid thundering herd.
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = TransientHttpFailure,
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(1),
                    MaxDelay = TimeSpan.FromSeconds(10),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ai84-resilience] Retry {args.AttemptNumber + 1} after {args.RetryDelay.TotalMilliseconds:F0}ms " +
                            $"(outcome: {args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString() ?? "unknown"}).");
                        return ValueTask.CompletedTask;
                    }
                })
                // Circuit breaker: if upstream is clearly down, stop hammering it for a while
                // and fail fast so the caller can show a useful error instead of waiting 3min.
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = TransientHttpFailure,
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30),
                    OnOpened = args =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ai84-resilience] Circuit OPENED for {args.BreakDuration.TotalSeconds:F0}s.");
                        return ValueTask.CompletedTask;
                    },
                    OnClosed = _ =>
                    {
                        System.Diagnostics.Debug.WriteLine("[ai84-resilience] Circuit CLOSED — recovered.");
                        return ValueTask.CompletedTask;
                    },
                    OnHalfOpened = _ =>
                    {
                        System.Diagnostics.Debug.WriteLine("[ai84-resilience] Circuit HALF-OPEN — probing.");
                        return ValueTask.CompletedTask;
                    }
                });

        /// <summary>
        /// Long-polling pipeline for AI84 job-status polling. The server is already
        /// responding (just slowly), so we keep retrying without tripping a circuit breaker.
        /// 5 attempts at 120s each is enough to ride out a real upstream stall.
        /// </summary>
        public static void ConfigureLongPollingPipeline(ResiliencePipelineBuilder<HttpResponseMessage> builder) =>
            builder
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(120),
                    OnTimeout = args =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ai84-polling] Attempt timed out after {args.Timeout.TotalSeconds:F0}s.");
                        return ValueTask.CompletedTask;
                    }
                })
                .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
                {
                    ShouldHandle = TransientHttpFailure,
                    MaxRetryAttempts = 5,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    MaxDelay = TimeSpan.FromSeconds(20),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[ai84-polling] Retry {args.AttemptNumber + 1} after {args.RetryDelay.TotalMilliseconds:F0}ms.");
                        return ValueTask.CompletedTask;
                    }
                });
    }
}
