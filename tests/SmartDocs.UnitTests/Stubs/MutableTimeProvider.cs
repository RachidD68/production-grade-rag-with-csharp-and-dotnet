namespace SmartDocs.UnitTests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> whose clock only moves when the test
/// calls <see cref="Advance"/>. Lets TTL-expiry and sliding-window logic be
/// asserted without real time. (Avoids a dependency on
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> — the project is package-free
/// for these phases.)
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
