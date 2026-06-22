using Microsoft.Extensions.DependencyInjection;

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
    /// Register a named <see cref="HttpClient"/> for an LLM provider with the
    /// standard resilience pipeline attached. The returned
    /// <see cref="IHttpClientFactory"/> client (resolved by
    /// <paramref name="clientName"/>) is the resilient transport to hand to the
    /// provider SDK / <c>IChatClient</c>.
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
        // circuit breaker + per-attempt timeout (Polly v8) onto the client.
        services.AddHttpClient(clientName)
            .AddStandardResilienceHandler();

        return services;
    }
}
