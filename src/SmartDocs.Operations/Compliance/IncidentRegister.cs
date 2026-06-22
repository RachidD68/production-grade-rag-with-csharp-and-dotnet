using System.Collections.Concurrent;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Operations.Compliance;

/// <summary>Incident severity, ordered low → high.</summary>
public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// A fixed-schema incident record. The shape is the "Cosmos document" the
/// production register stores; offline it lives in an in-memory store behind
/// <see cref="IIncidentStore"/>.
/// </summary>
/// <param name="IncidentId">Stable id (caller-supplied or generated).</param>
/// <param name="Severity">How bad it is.</param>
/// <param name="Scope">What blast radius it touched (tenant, silo, "global", …).</param>
/// <param name="RootCause">Post-analysis root cause.</param>
/// <param name="Mitigation">What was done to contain / fix it.</param>
/// <param name="EvidencePointers">Links/ids into logs, traces, audit rows backing the record.</param>
/// <param name="DetectedAt">When the incident was first detected.</param>
/// <param name="Resolved">Whether it is closed.</param>
public sealed record Incident(
    string IncidentId,
    IncidentSeverity Severity,
    string Scope,
    string RootCause,
    string Mitigation,
    IReadOnlyList<string> EvidencePointers,
    DateTimeOffset DetectedAt,
    bool Resolved = false);

/// <summary>Persistence seam for incidents. Production impl is a Cosmos container.</summary>
public interface IIncidentStore
{
    Task AddAsync(Incident incident, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Incident>> QueryAsync(CancellationToken cancellationToken = default);
}

/// <summary>In-memory <see cref="IIncidentStore"/> for samples and tests.</summary>
public sealed class InMemoryIncidentStore : IIncidentStore
{
    private readonly ConcurrentDictionary<string, Incident> _incidents = new(StringComparer.Ordinal);

    public Task AddAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incidents[incident.IncidentId] = incident;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Incident>> QueryAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Incident> snapshot = [.. _incidents.Values.OrderBy(i => i.DetectedAt)];
        return Task.FromResult(snapshot);
    }
}

/// <summary>
/// The post-incident register. Records incidents on a fixed schema and answers
/// queries / reports over them. Security incidents raised through the Ch 23
/// <see cref="ISecurityAlertSink"/> path can flow straight in via
/// <see cref="RecordSecurityIncidentAsync"/>, which adapts a
/// <see cref="SecurityIncident"/> onto the register's <see cref="Incident"/>
/// schema.
/// </summary>
public sealed class IncidentRegister
{
    private readonly IIncidentStore _store;
    private readonly TimeProvider _timeProvider;

    public IncidentRegister(IIncidentStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Record a fully-analysed incident.</summary>
    public async Task RecordAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(incident);
        await _store.AddAsync(incident, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adapt a Ch 23 <see cref="SecurityIncident"/> (kind / detail / detected-at)
    /// onto the register schema and record it. The mapped severity defaults to
    /// <see cref="IncidentSeverity.High"/> for security incidents; root cause and
    /// mitigation are seeded from the alert and filled in during analysis.
    /// </summary>
    public async Task<Incident> RecordSecurityIncidentAsync(
        SecurityIncident securityIncident,
        IncidentSeverity severity = IncidentSeverity.High,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(securityIncident);

        var incident = new Incident(
            IncidentId: $"sec-{securityIncident.DetectedAtUtc.ToUnixTimeMilliseconds()}-{securityIncident.Kind}",
            Severity: severity,
            Scope: securityIncident.Kind,
            RootCause: securityIncident.Detail,
            Mitigation: "Pending analysis.",
            EvidencePointers: [$"security-alert:{securityIncident.Kind}"],
            DetectedAt: securityIncident.DetectedAtUtc,
            Resolved: false);

        await _store.AddAsync(incident, cancellationToken).ConfigureAwait(false);
        return incident;
    }

    /// <summary>All incidents, oldest first.</summary>
    public Task<IReadOnlyList<Incident>> QueryAsync(CancellationToken cancellationToken = default) =>
        _store.QueryAsync(cancellationToken);

    /// <summary>Currently-open (unresolved) incidents.</summary>
    public async Task<IReadOnlyList<Incident>> OpenIncidentsAsync(CancellationToken cancellationToken = default)
    {
        var all = await _store.QueryAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Where(i => !i.Resolved)];
    }

    /// <summary>A small severity-bucketed report for the dashboard / a weekly summary.</summary>
    public async Task<IncidentReport> ReportAsync(CancellationToken cancellationToken = default)
    {
        var all = await _store.QueryAsync(cancellationToken).ConfigureAwait(false);
        var open = all.Where(i => !i.Resolved).ToList();
        var bySeverity = all
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.Count());
        return new IncidentReport(
            Total: all.Count,
            Open: open.Count,
            BySeverity: bySeverity,
            GeneratedAt: _timeProvider.GetUtcNow());
    }
}

/// <summary>A severity-bucketed incident summary.</summary>
public sealed record IncidentReport(
    int Total,
    int Open,
    IReadOnlyDictionary<IncidentSeverity, int> BySeverity,
    DateTimeOffset GeneratedAt);
