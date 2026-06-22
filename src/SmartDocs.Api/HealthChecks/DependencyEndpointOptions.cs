namespace SmartDocs.Api.HealthChecks;

/// <summary>
/// Endpoints the readiness probes reach out to (Ch 25 §"Health checks"). Bound
/// from the <c>SmartDocs:Dependencies</c> configuration section. Every endpoint is
/// optional: a blank value means "not configured in this environment", and the
/// matching health check reports <c>Degraded</c> rather than failing — so the dev
/// inner loop and CI, where none of these run, still report a sane readiness
/// state instead of a hard <c>Unhealthy</c>.
/// </summary>
public sealed class DependencyEndpointOptions
{
    /// <summary>Configuration section bound to this class.</summary>
    public const string SectionName = "SmartDocs:Dependencies";

    /// <summary>Qdrant base URL, e.g. <c>http://localhost:6333</c>. The probe hits <c>/healthz</c>.</summary>
    public string? QdrantEndpoint { get; set; }

    /// <summary>Azure OpenAI resource endpoint. The probe issues a lightweight authenticated-less reachability check.</summary>
    public string? OpenAiEndpoint { get; set; }

    /// <summary>Redis connection string / host. When blank, the Redis probe degrades.</summary>
    public string? RedisConnection { get; set; }

    /// <summary>Cosmos DB account endpoint. The probe hits the account root.</summary>
    public string? CosmosEndpoint { get; set; }
}
