namespace SmartDocs.Operations.Compliance;

/// <summary>
/// One dashboard tile. Carries a display value, an optional secondary value
/// (e.g. a 7-day trend alongside today's figure), and a <see cref="LinkKey"/>
/// the UI resolves to the underlying records (audit rows, incident ids, …) so a
/// reviewer can drill in.
/// </summary>
public sealed record DashboardTile(string Title, string Value, string? Secondary, string LinkKey);

/// <summary>
/// The six-tile compliance snapshot the chapter lists. A data structure, not UI:
/// the front end renders the tiles. Each tile links to its backing records.
/// </summary>
public sealed record DashboardSnapshot(
    DateTimeOffset GeneratedAt,
    DashboardTile Faithfulness,
    DashboardTile PiiRedactionRate,
    DashboardTile TenantLeakPrevention,
    DashboardTile ActiveErasureRequests,
    DashboardTile ConsentCoverage,
    DashboardTile OpenIncidents)
{
    /// <summary>All six tiles in display order.</summary>
    public IReadOnlyList<DashboardTile> Tiles =>
    [
        Faithfulness,
        PiiRedactionRate,
        TenantLeakPrevention,
        ActiveErasureRequests,
        ConsentCoverage,
        OpenIncidents,
    ];
}

/// <summary>
/// The raw figures the dashboard renders. Gathered from the other Compliance
/// components (the sampler's rolling average, the redactor's counters, the
/// tenant guard's block count, the erasure backlog, the consent registry, the
/// incident register) and handed in so the dashboard itself stays a pure
/// projection — trivially testable.
/// </summary>
public sealed record ComplianceMetrics(
    double FaithfulnessToday,
    double Faithfulness7DayTrend,
    double PiiRedactionRate,
    int TenantLeakPreventionCount,
    int ActiveErasureRequests,
    ConsentCoverage ConsentCoverage,
    int OpenIncidents);

/// <summary>
/// Projects <see cref="ComplianceMetrics"/> into the six-tile
/// <see cref="DashboardSnapshot"/>. Pure and synchronous; the caller is
/// responsible for gathering the metrics from the live components.
/// </summary>
public sealed class ComplianceDashboard
{
    private readonly TimeProvider _timeProvider;

    public ComplianceDashboard(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DashboardSnapshot Build(ComplianceMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        return new DashboardSnapshot(
            GeneratedAt: _timeProvider.GetUtcNow(),
            Faithfulness: new DashboardTile(
                "Faithfulness (today)",
                metrics.FaithfulnessToday.ToString("0.###", inv),
                $"7-day {metrics.Faithfulness7DayTrend.ToString("0.###", inv)}",
                "faithfulness/samples"),
            PiiRedactionRate: new DashboardTile(
                "PII redaction rate",
                metrics.PiiRedactionRate.ToString("0.##%", inv),
                null,
                "redaction/events"),
            TenantLeakPrevention: new DashboardTile(
                "Tenant leaks prevented",
                metrics.TenantLeakPreventionCount.ToString(inv),
                null,
                "tenant-guard/blocks"),
            ActiveErasureRequests: new DashboardTile(
                "Active erasure requests",
                metrics.ActiveErasureRequests.ToString(inv),
                null,
                "gdpr/requests"),
            ConsentCoverage: new DashboardTile(
                "Consent coverage",
                metrics.ConsentCoverage.Fraction.ToString("0.##%", inv),
                $"{metrics.ConsentCoverage.Covered}/{metrics.ConsentCoverage.Total} ({metrics.ConsentCoverage.Scope})",
                "consent/coverage"),
            OpenIncidents: new DashboardTile(
                "Open incidents",
                metrics.OpenIncidents.ToString(inv),
                null,
                "incidents/open"));
    }
}
