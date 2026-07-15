using Microsoft.Extensions.Logging;
using SmartDocs.Core.Documents;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security.Retrieval;

/// <summary>
/// Authoritative chunk-id → tenant map. The vector store's metadata filter is
/// the first line of defense against cross-tenant leakage; this index is the
/// independent second check used by <see cref="TenantGuard"/>, so a single
/// mis-built filter cannot leak another tenant's data.
/// </summary>
public interface ITenantIndex
{
    /// <summary>
    /// Looks up the owning tenant for <paramref name="chunkId"/>. Returns false
    /// when the chunk is unknown (which the guard treats as "drop").
    /// </summary>
    bool TryGetTenant(string chunkId, out string tenant);
}

/// <summary>In-memory <see cref="ITenantIndex"/> backed by a dictionary.</summary>
public sealed class DictionaryTenantIndex : ITenantIndex
{
    private readonly IReadOnlyDictionary<string, string> _map;

    public DictionaryTenantIndex(IReadOnlyDictionary<string, string> chunkToTenant)
    {
        ArgumentNullException.ThrowIfNull(chunkToTenant);
        _map = chunkToTenant;
    }

    public bool TryGetTenant(string chunkId, out string tenant)
    {
        ArgumentNullException.ThrowIfNull(chunkId);
        if (_map.TryGetValue(chunkId, out var found))
        {
            tenant = found;
            return true;
        }
        tenant = string.Empty;
        return false;
    }
}

/// <summary>
/// Post-retrieval cross-tenant leak guard. For every retrieved chunk it
/// re-checks the owning tenant against an authoritative <see cref="ITenantIndex"/>
/// and drops any chunk that belongs to a different tenant (or is unknown),
/// raising a <c>tenant-leak-prevented</c> incident. This catches the case where
/// the store-side filter was bypassed, mis-built, or the chunk was injected.
/// </summary>
public sealed partial class TenantGuard
{
    private readonly ITenantIndex _chunkIndex;
    private readonly ISecurityAlertSink _alert;
    private readonly ILogger<TenantGuard>? _logger;

    public TenantGuard(
        ITenantIndex chunkIndex,
        ISecurityAlertSink alert,
        ILogger<TenantGuard>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(chunkIndex);
        ArgumentNullException.ThrowIfNull(alert);
        _chunkIndex = chunkIndex;
        _alert = alert;
        _logger = logger;
    }

    /// <summary>
    /// Returns only the results whose chunk is owned by
    /// <paramref name="expectedTenant"/>; raises an incident for each dropped chunk.
    /// </summary>
    public async Task<IReadOnlyList<RetrievalResult>> VerifyAsync(
        IReadOnlyList<RetrievalResult> results,
        string expectedTenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentException.ThrowIfNullOrEmpty(expectedTenant);

        var survivors = new List<RetrievalResult>(results.Count);
        foreach (var result in results)
        {
            var chunkId = result.Chunk.ChunkId;
            var known = _chunkIndex.TryGetTenant(chunkId, out var tenant);
            if (known && string.Equals(tenant, expectedTenant, StringComparison.Ordinal))
            {
                survivors.Add(result);
                continue;
            }

            var owner = known ? tenant : "<unknown>";
            if (_logger is not null)
            {
                Log.DroppedChunk(_logger, chunkId, owner, expectedTenant);
            }

            await _alert.RaiseAsync(
                new SecurityIncident(
                    Kind: "tenant-leak-prevented",
                    Detail: $"Chunk '{chunkId}' owned by '{owner}' was retrieved for tenant '{expectedTenant}' and dropped.",
                    DetectedAtUtc: DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        return survivors;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Dropped chunk {ChunkId} owned by {Owner}; expected tenant {Expected}.")]
        public static partial void DroppedChunk(ILogger logger, string chunkId, string owner, string expected);
    }
}
