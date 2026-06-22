using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// DI wiring for the SmartDocs health checks (Ch 25). Splits the two probe
/// classes Kubernetes / Azure Container Apps expect:
/// <list type="bullet">
///   <item><description>
///     a <c>self</c> liveness check tagged <c>live</c> — "the process is up and the
///     event loop is turning" — which must NOT depend on any external service, or a
///     dependency outage would get the pod killed and restarted pointlessly;
///   </description></item>
///   <item><description>
///     the dependency readiness checks tagged <c>ready</c> — "can this instance
///     actually serve traffic right now" — which the load balancer / ingress uses
///     to decide whether to route to the pod.
///   </description></item>
/// </list>
/// The Ch 25 Bicep points its container probe at <c>/health/ready</c>; the two
/// endpoints are mapped in <c>Program.cs</c> by tag predicate.
/// </summary>
public static class SmartDocsHealthChecks
{
    /// <summary>The tag marking the liveness check(s).</summary>
    public const string LiveTag = "live";

    /// <summary>The tag marking the readiness (dependency) checks.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The name of the short-timeout <see cref="HttpClient"/> the HTTP probes use.</summary>
    public const string ProbeClientName = "health-probe";

    // A deliberately short probe timeout: a readiness check must answer fast, and a
    // slow dependency should read as "not ready", not hang the probe.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Register the dependency endpoint options, the probe <see cref="HttpClient"/>,
    /// and the live + ready health checks.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="configuration">Application configuration (binds <see cref="DependencyEndpointOptions"/>).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSmartDocsHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DependencyEndpointOptions>()
            .Bind(configuration.GetSection(DependencyEndpointOptions.SectionName));

        // One short-timeout client shared by the HTTP probes (Qdrant/OpenAI/Cosmos).
        services.AddHttpClient(ProbeClientName, client => client.Timeout = ProbeTimeout);

        services.AddHealthChecks()
            // Liveness: a trivial always-healthy self check. No dependency touched.
            .AddCheck("self", () => HealthCheckResult.Healthy("Process is live."), tags: [LiveTag])
            // Readiness: each dependency probe degrades (not throws) when offline.
            // The HTTP probes pull the named short-timeout client from the factory;
            // Redis uses the app's IDistributedCache. All resolved from the container.
            .AddCheck<QdrantHealthCheck>("qdrant", tags: [ReadyTag])
            .AddCheck<OpenAiHealthCheck>("openai", tags: [ReadyTag])
            .AddCheck<RedisHealthCheck>("redis", tags: [ReadyTag])
            .AddCheck<CosmosHealthCheck>("cosmos", tags: [ReadyTag]);

        return services;
    }
}
