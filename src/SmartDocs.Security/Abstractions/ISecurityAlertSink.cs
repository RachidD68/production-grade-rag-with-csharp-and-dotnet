namespace SmartDocs.Security.Abstractions;

/// <summary>
/// A single security event worth surfacing to operators or a SIEM. The
/// <paramref name="Kind"/> is a stable machine-readable category (for example
/// <c>"ingest-injection-quarantined"</c> or <c>"tenant-leak-prevented"</c>);
/// <paramref name="Detail"/> is a free-form human description.
/// </summary>
public sealed record SecurityIncident(string Kind, string Detail, DateTimeOffset DetectedAtUtc);

/// <summary>
/// Sink for <see cref="SecurityIncident"/> notifications. Production
/// implementations fan out to Azure Monitor / a SIEM / PagerDuty; tests capture
/// incidents in memory.
/// </summary>
public interface ISecurityAlertSink
{
    /// <summary>Raises <paramref name="incident"/> to the configured destination.</summary>
    Task RaiseAsync(SecurityIncident incident, CancellationToken cancellationToken = default);
}
