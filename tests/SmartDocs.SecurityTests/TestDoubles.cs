using SmartDocs.Security.Abstractions;

namespace SmartDocs.SecurityTests;

/// <summary>Captures every raised <see cref="SecurityIncident"/> for assertion.</summary>
internal sealed class CapturingAlertSink : ISecurityAlertSink
{
    public List<SecurityIncident> Incidents { get; } = [];

    public Task RaiseAsync(SecurityIncident incident, CancellationToken cancellationToken = default)
    {
        Incidents.Add(incident);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Deterministic <see cref="TimeProvider"/> whose clock only advances when the
/// test calls <see cref="Advance"/>. Used to drive the sliding-window
/// rate limiter and the audit checkpoint clock without real time.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
