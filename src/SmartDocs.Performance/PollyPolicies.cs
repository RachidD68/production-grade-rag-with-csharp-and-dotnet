using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace SmartDocs.Performance;

/// <summary>
/// Pre-built Polly v8 ResiliencePipelines for SmartDocs external dependencies.
/// Wire via Microsoft.Extensions.Http.Resilience for HttpClient-backed paths
/// (LLM provider calls); use the pipelines directly for non-HTTP retries.
/// </summary>
public static class PollyPolicies
{
    /// <summary>LLM API: 3 retries with exponential backoff + circuit breaker after 5 consecutive failures.</summary>
    public static ResiliencePipeline LlmApi() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(1),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(20),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();

    /// <summary>Vector DB: 2 retries, 5s timeout. Cheap to retry; should rarely fail.</summary>
    public static ResiliencePipeline VectorDb() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Linear,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
            })
            .AddTimeout(TimeSpan.FromSeconds(5))
            .Build();

    /// <summary>Graph DB: 2 retries, 3s timeout.</summary>
    public static ResiliencePipeline GraphDb() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Linear,
                Delay = TimeSpan.FromMilliseconds(200),
                ShouldHandle = new PredicateBuilder().Handle<Exception>(IsTransient),
            })
            .AddTimeout(TimeSpan.FromSeconds(3))
            .Build();

    /// <summary>Heuristic for "transient" — HTTP 429/5xx, timeouts, broken pipes.</summary>
    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException
        or TimeoutRejectedException
        or BrokenCircuitException
        || ex.GetType().Name.Contains("RateLimit", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("429", StringComparison.Ordinal);
}
