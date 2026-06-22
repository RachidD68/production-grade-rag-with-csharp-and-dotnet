using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SmartDocs.Performance.Cost;

namespace SmartDocs.Performance.Observability;

/// <summary>
/// OpenTelemetry wiring for the SmartDocs performance instrumentation (Ch 21).
/// Registers the pipeline <see cref="PipelineActivitySource"/> trace source and
/// the <see cref="CostMeter"/> cost-counter meter so the per-stage spans and the
/// per-tenant cost metric flow to whatever exporter the host configures (the
/// Aspire dashboard, an OTLP collector, …).
/// </summary>
public static class SmartDocsTelemetry
{
    /// <summary>
    /// Register the SmartDocs activity source and cost meter on the OpenTelemetry
    /// trace and metric pipelines. Call after <c>AddOpenTelemetry()</c> or use it
    /// as the single entry point — it adds <c>AddOpenTelemetry()</c> itself, so it
    /// is safe to call standalone.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSmartDocsTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(PipelineActivitySource.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(CostMeter.MeterName));

        return services;
    }
}
