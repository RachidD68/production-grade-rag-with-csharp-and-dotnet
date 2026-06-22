using System.Diagnostics.Metrics;

namespace SmartDocs.Performance.Cost;

/// <summary>
/// Per-token pricing (USD) used to turn token usage into a dollar figure.
/// Mid-2026 Azure OpenAI list prices — they move frequently; treat these as a
/// configurable default, not a constant of nature. Prices are per single token
/// (the published $/1M figure divided by 1,000,000).
/// </summary>
/// <param name="InputPerToken">USD cost of one input (prompt) token.</param>
/// <param name="OutputPerToken">USD cost of one output (completion) token.</param>
/// <param name="EmbeddingPerToken">USD cost of one embedding-input token.</param>
public sealed record TokenPricing(
    double InputPerToken,
    double OutputPerToken,
    double EmbeddingPerToken)
{
    /// <summary>A reasonable mid-2026 default (a small generation model + a small embedding model).</summary>
    public static TokenPricing Default { get; } = new(
        InputPerToken: 0.15 / 1_000_000,
        OutputPerToken: 0.60 / 1_000_000,
        EmbeddingPerToken: 0.02 / 1_000_000);
}

/// <summary>
/// The shared <see cref="Meter"/> and cost <see cref="Counter{T}"/> the cost
/// decorators emit to (Ch 21). One process-wide meter named
/// <c>SmartDocs.Cost</c>; every recorded measurement is tagged with the tenant
/// so spend can be sliced per customer in the dashboard. Subscribe via
/// <see cref="Observability.SmartDocsTelemetry.AddSmartDocsTelemetry"/>.
/// </summary>
public static class CostMeter
{
    /// <summary>The meter name external listeners subscribe to.</summary>
    public const string MeterName = "SmartDocs.Cost";

    /// <summary>The tag key carrying the tenant id on every cost measurement.</summary>
    public const string TenantTag = "tenant";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>USD cost counter. Increment with the dollar cost of each LLM/embedding call, tagged by tenant.</summary>
    public static readonly Counter<double> CostUsd =
        Meter.CreateCounter<double>("smartdocs.cost.usd", unit: "USD", description: "LLM and embedding spend in USD.");

    /// <summary>Record <paramref name="costUsd"/> against <paramref name="tenant"/>.</summary>
    public static void RecordCost(double costUsd, string tenant)
    {
        if (costUsd <= 0)
        {
            return;
        }
        CostUsd.Add(costUsd, new KeyValuePair<string, object?>(TenantTag, tenant));
    }
}
