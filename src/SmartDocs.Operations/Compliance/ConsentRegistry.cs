using System.Collections.Concurrent;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// A single per-user, per-scope consent grant. The high-risk variant of the
/// system tracks consent at <see cref="Scope"/> granularity so a subject can
/// revoke one purpose (e.g. <c>analytics</c>) while keeping another (e.g.
/// <c>core-search</c>).
/// </summary>
/// <param name="UserId">The subject the consent belongs to.</param>
/// <param name="Scope">The purpose the consent covers.</param>
/// <param name="Version">The consent-document version the subject agreed to.</param>
/// <param name="Granted"><see langword="true"/> when active, <see langword="false"/> after revoke.</param>
/// <param name="UpdatedAt">When this grant last changed.</param>
public sealed record ConsentGrant(
    string UserId,
    string Scope,
    string Version,
    bool Granted,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Per-user, per-scope consent ledger. Records the consent-document version each
/// subject agreed to for each scope, exposes the current grants, supports
/// per-scope revoke (the high-risk variant's distinguishing feature), and reports
/// coverage for the compliance dashboard. In-memory here; production persists the
/// same shape.
/// </summary>
public sealed class ConsentRegistry
{
    // userId -> scope -> grant
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConsentGrant>> _grants =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public ConsentRegistry(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Record (or update) a granted consent for <paramref name="userId"/> on <paramref name="scope"/>.</summary>
    public Task RecordAsync(string userId, string scope, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var scopes = _grants.GetOrAdd(userId, _ => new ConcurrentDictionary<string, ConsentGrant>(StringComparer.Ordinal));
        scopes[scope] = new ConsentGrant(userId, scope, version, Granted: true, _timeProvider.GetUtcNow());
        return Task.CompletedTask;
    }

    /// <summary>Current grants for a user across all scopes (granted and revoked).</summary>
    public Task<IReadOnlyList<ConsentGrant>> GetCurrentAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        IReadOnlyList<ConsentGrant> result = _grants.TryGetValue(userId, out var scopes)
            ? [.. scopes.Values.OrderBy(g => g.Scope, StringComparer.Ordinal)]
            : [];
        return Task.FromResult(result);
    }

    /// <summary>
    /// Revoke a single scope's consent for a user (the high-risk per-scope
    /// revoke). Other scopes are untouched. No-op when the user/scope is unknown.
    /// Returns <see langword="true"/> when a grant was flipped to revoked.
    /// </summary>
    public Task<bool> RevokeAsync(string userId, string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        if (_grants.TryGetValue(userId, out var scopes) &&
            scopes.TryGetValue(scope, out var existing) &&
            existing.Granted)
        {
            scopes[scope] = existing with { Granted = false, UpdatedAt = _timeProvider.GetUtcNow() };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Coverage over a known user population for a given scope: the fraction with
    /// an active grant on <paramref name="scope"/>. Drives the dashboard's consent
    /// tile.
    /// </summary>
    public Task<ConsentCoverage> CoverageAsync(
        IReadOnlyCollection<string> population,
        string scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var covered = population.Count(userId =>
            _grants.TryGetValue(userId, out var scopes) &&
            scopes.TryGetValue(scope, out var grant) &&
            grant.Granted);

        var fraction = population.Count == 0 ? 1.0 : (double)covered / population.Count;
        return Task.FromResult(new ConsentCoverage(scope, covered, population.Count, fraction));
    }
}

/// <summary>Consent coverage for one scope over a population.</summary>
public sealed record ConsentCoverage(string Scope, int Covered, int Total, double Fraction);
