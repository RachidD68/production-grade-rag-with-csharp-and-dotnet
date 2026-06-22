using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace SmartDocs.Performance;

/// <summary>
/// The DI-idiomatic resilience path (Ch 21, §"resilience"). <see cref="PollyPolicies"/>
/// hand-builds a Polly v8 pipeline so you can see what is inside; this helper is
/// the one-liner you actually ship: <c>AddStandardResilienceHandler()</c> from
/// <c>Microsoft.Extensions.Http.Resilience</c> wraps a typed/named
/// <see cref="HttpClient"/> with the standard pipeline — rate limiter, total-request
/// timeout, retry, circuit breaker, and per-attempt timeout — in the correct order.
/// Both exist on purpose: the hand-rolled builder teaches the mechanics, this
/// teaches the production default. (<c>Microsoft.Extensions.Http.Polly</c> is
/// deprecated; this is its replacement.)
/// </summary>
public static class ResilienceWiring
{
    /// <summary>
    /// The Ch 25 production resilience profile for the Azure OpenAI + embedding
    /// HTTP clients. These are the exact knobs the chapter's "Surviving the
    /// provider" section sets; they are surfaced as constants so a unit test can
    /// assert the wired pipeline matches the manuscript, and so a reader can see
    /// every number in one place.
    /// </summary>
    public static class StandardProfile
    {
        /// <summary>Retry attempts after the initial try (<c>Retry.MaxRetryAttempts</c>).</summary>
        public const int MaxRetryAttempts = 4;

        /// <summary>Per-attempt timeout — each individual try is abandoned after this.</summary>
        public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The circuit-breaker failure ratio that trips the breaker (0.5 = 50%).</summary>
        public const double CircuitBreakerFailureRatio = 0.5;

        /// <summary>
        /// The rolling window the breaker samples failures over. The standard
        /// options validator requires this to be at least twice
        /// <see cref="AttemptTimeout"/>; with a 30 s attempt timeout the floor is
        /// 60 s, so this is set to 60 s (the chapter calls for "30 s sampling" but
        /// the framework clamps the minimum — see the XML remark on
        /// <see cref="AddResilientLlmHttpClient"/>).
        /// </summary>
        public static readonly TimeSpan CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(60);

        /// <summary>The overall budget for one logical request including all retries.</summary>
        public static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(100);
    }

    /// <summary>
    /// Register a named <see cref="HttpClient"/> for an LLM provider with the
    /// standard resilience pipeline attached. The returned
    /// <see cref="IHttpClientFactory"/> client (resolved by
    /// <paramref name="clientName"/>) is the resilient transport to hand to the
    /// provider SDK / <c>IChatClient</c>.
    ///
    /// <para>
    /// The pipeline is configured to the Ch 25 <see cref="StandardProfile"/>:
    /// 4 retries with jitter on an exponential backoff, a 30-second per-attempt
    /// timeout, a circuit breaker that opens at a 50% failure ratio, and a
    /// 100-second total-request budget. One caveat the framework enforces: the
    /// breaker's <c>SamplingDuration</c> must be ≥ 2× the attempt timeout, so the
    /// chapter's "30 s sampling" is clamped up to 60 s — anything lower fails the
    /// built-in options validation.
    /// </para>
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="clientName">The logical name of the HttpClient. Defaults to <c>llm</c>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddResilientLlmHttpClient(
        this IServiceCollection services,
        string clientName = "llm")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

        // The whole point: one call wires rate limiter + total timeout + retry +
        // circuit breaker + per-attempt timeout (Polly v8) onto the client, here
        // pinned to the production profile rather than the framework defaults.
        services.AddHttpClient(clientName)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = StandardProfile.MaxRetryAttempts;
                options.Retry.UseJitter = true;
                options.Retry.BackoffType = DelayBackoffType.Exponential;

                options.AttemptTimeout.Timeout = StandardProfile.AttemptTimeout;

                options.CircuitBreaker.FailureRatio = StandardProfile.CircuitBreakerFailureRatio;
                options.CircuitBreaker.SamplingDuration = StandardProfile.CircuitBreakerSamplingDuration;

                options.TotalRequestTimeout.Timeout = StandardProfile.TotalRequestTimeout;
            });

        return services;
    }
}
