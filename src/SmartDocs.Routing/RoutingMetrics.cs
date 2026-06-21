using System.Diagnostics.Metrics;

namespace SmartDocs.Routing;

/// <summary>
/// OpenTelemetry-compatible instruments for the routing layer, grouped under the
/// <c>SmartDocs.Routing</c> meter. Wrap a router with
/// <see cref="InstrumentedRouter"/> to record these on every decision; point a
/// <see cref="MeterListener"/> or the OpenTelemetry metrics SDK at the
/// <see cref="MeterName"/> meter to export them.
/// </summary>
public sealed class RoutingMetrics : IDisposable
{
    /// <summary>The meter name to subscribe to when collecting routing metrics.</summary>
    public const string MeterName = "SmartDocs.Routing";

    private readonly Meter _meter;

    /// <summary>Count of routing decisions, tagged by <c>silo</c> and <c>strategy</c>.</summary>
    public Counter<long> Routes { get; }

    /// <summary>Distribution of routing confidence scores, tagged by <c>strategy</c>.</summary>
    public Histogram<double> Confidence { get; }

    /// <summary>Count of escalations to an LLM/semantic strategy, tagged by <c>strategy</c>.</summary>
    public Counter<long> LlmEscalations { get; }

    /// <summary>Create the metrics, registering instruments on the <see cref="MeterName"/> meter.</summary>
    public RoutingMetrics()
    {
        _meter = new Meter(MeterName);
        Routes = _meter.CreateCounter<long>(
            "smartdocs.routing.routes",
            unit: "{route}",
            description: "Number of routing decisions, by silo and strategy.");
        Confidence = _meter.CreateHistogram<double>(
            "smartdocs.routing.confidence",
            unit: "{score}",
            description: "Routing confidence score per decision.");
        LlmEscalations = _meter.CreateCounter<long>(
            "smartdocs.routing.llm_escalations",
            unit: "{escalation}",
            description: "Number of decisions that escalated to an LLM/semantic strategy.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}

/// <summary>
/// Decorator over an inner <see cref="IQueryRouter"/> that records
/// <see cref="RoutingMetrics"/> on every <see cref="RouteAsync"/> call: one
/// silo-counter increment per returned silo, the confidence histogram, and an
/// escalation count when the inner strategy is LLM- or embedding-based. The
/// routing behaviour is unchanged — the decision is passed straight through.
/// </summary>
public sealed class InstrumentedRouter : IQueryRouter
{
    private readonly IQueryRouter _inner;
    private readonly RoutingMetrics _metrics;

    public InstrumentedRouter(IQueryRouter inner, RoutingMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(metrics);
        _inner = inner;
        _metrics = metrics;
    }

    public string Strategy => _inner.Strategy;

    public async Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        var decision = await _inner.RouteAsync(query, cancellationToken).ConfigureAwait(false);

        var strategyTag = new KeyValuePair<string, object?>("strategy", decision.Strategy);

        _metrics.Confidence.Record(decision.Confidence, strategyTag);

        foreach (var silo in decision.Silos)
        {
            _metrics.Routes.Add(
                1,
                new KeyValuePair<string, object?>("silo", silo),
                strategyTag);
        }

        if (IsEscalation(decision.Strategy))
        {
            _metrics.LlmEscalations.Add(1, strategyTag);
        }

        return decision;
    }

    // The inner strategy string carries the routing method; "llm-classifier" and
    // "semantic" (embedding) both count as escalations beyond cheap rules. The
    // MultiSourceRouter composes these names, so a substring match catches the
    // composite "multi(rule-based+llm-classifier)" form too.
    private static bool IsEscalation(string strategy) =>
        strategy.Contains("llm-classifier", StringComparison.Ordinal) ||
        strategy.Contains("semantic", StringComparison.Ordinal);
}
