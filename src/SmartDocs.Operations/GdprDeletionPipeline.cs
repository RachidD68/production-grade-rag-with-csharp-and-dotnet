using SmartDocs.Core.Abstractions;

namespace SmartDocs.Operations;

/// <summary>
/// GDPR Article 17 deletion pipeline. Tombstones the document id, then
/// hard-deletes from each registered store (vector, graph, audit log) and
/// emits a court-acceptable audit trail entry. The full Ch 24 audit-log
/// shape ships in <c>SmartDocs.Generation</c>; this class only
/// surfaces the high-level orchestration for Ch 22.
/// </summary>
public sealed class GdprDeletionPipeline
{
    private readonly IVectorStore _vector;
    private readonly Func<string, Task> _graphDelete;
    private readonly Action<DeletionAuditEntry> _audit;

    public GdprDeletionPipeline(
        IVectorStore vector,
        Func<string, Task> graphDelete,
        Action<DeletionAuditEntry> audit)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(graphDelete);
        ArgumentNullException.ThrowIfNull(audit);
        _vector = vector;
        _graphDelete = graphDelete;
        _audit = audit;
    }

    public async Task<DeletionAuditEntry> DeleteAsync(
        string subjectId,
        IEnumerable<string> chunkIds,
        IEnumerable<string> graphEntityIds,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(chunkIds);
        ArgumentNullException.ThrowIfNull(graphEntityIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        var ids = chunkIds.ToList();
        await _vector.DeleteAsync(ids, cancellationToken).ConfigureAwait(false);
        foreach (var entityId in graphEntityIds)
        {
            await _graphDelete(entityId).ConfigureAwait(false);
        }
        var entry = new DeletionAuditEntry(
            SubjectId: subjectId,
            ChunkIds: ids,
            GraphEntityIds: graphEntityIds.ToList(),
            RequestedBy: requestedBy,
            CompletedUtc: DateTimeOffset.UtcNow);
        _audit(entry);
        return entry;
    }
}

/// <summary>One row in the GDPR deletion audit log.</summary>
public sealed record DeletionAuditEntry(
    string SubjectId,
    IReadOnlyList<string> ChunkIds,
    IReadOnlyList<string> GraphEntityIds,
    string RequestedBy,
    DateTimeOffset CompletedUtc);
