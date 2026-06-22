using System.Collections.Concurrent;
using SmartDocs.Core.Documents;
using SmartDocs.Generation.Citations;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// Read seam over the EU AI Act audit log. The Ch 24 <see cref="AuditLogger"/>
/// is write-only (it appends <see cref="AuditEntry"/> rows to an injected sink);
/// auditing a historical answer needs the inverse — fetch the row a given query
/// produced. In production this is a point read against the Cosmos container the
/// logger's sink writes to, keyed by query id. Offline we keep the rows in a
/// dictionary.
/// </summary>
public interface IAuditRecordStore
{
    /// <summary>
    /// Resolve the audit row recorded for <paramref name="queryId"/>, or
    /// <see langword="null"/> when no such row exists.
    /// </summary>
    Task<AuditEntry?> GetAsync(string queryId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory <see cref="IAuditRecordStore"/> for samples and tests. The
/// production adapter reads the same <see cref="AuditEntry"/> shape from the
/// Cosmos container the <see cref="AuditLogger"/> sink writes to.
/// </summary>
public sealed class InMemoryAuditRecordStore : IAuditRecordStore
{
    private readonly ConcurrentDictionary<string, AuditEntry> _rows = new(StringComparer.Ordinal);

    /// <summary>Record (or overwrite) the audit row for <paramref name="queryId"/>.</summary>
    public void Put(string queryId, AuditEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        ArgumentNullException.ThrowIfNull(entry);
        _rows[queryId] = entry;
    }

    public Task<AuditEntry?> GetAsync(string queryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);
        _rows.TryGetValue(queryId, out var entry);
        return Task.FromResult(entry);
    }
}

/// <summary>
/// By-id lookup over document metadata. <see cref="DocumentChunk.Metadata"/>
/// already denormalises the parent <see cref="DocumentMetadata"/> onto every
/// chunk, so the auditor reads the source uri / modified-at straight off the
/// chunk; this seam exists for the cases that need the canonical document record
/// independent of any one chunk. Offline impl is a dictionary; production reads
/// the document container.
/// </summary>
public interface IDocumentMetadataStore
{
    /// <summary>
    /// Resolve the metadata for <paramref name="documentId"/>, or
    /// <see langword="null"/> when unknown.
    /// </summary>
    Task<DocumentMetadata?> GetAsync(string documentId, CancellationToken cancellationToken = default);
}

/// <summary>In-memory <see cref="IDocumentMetadataStore"/> for samples and tests.</summary>
public sealed class InMemoryDocumentMetadataStore : IDocumentMetadataStore
{
    private readonly ConcurrentDictionary<string, DocumentMetadata> _docs = new(StringComparer.Ordinal);

    /// <summary>Record (or overwrite) the metadata for its <see cref="DocumentMetadata.Id"/>.</summary>
    public void Put(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _docs[metadata.Id] = metadata;
    }

    public Task<DocumentMetadata?> GetAsync(string documentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        _docs.TryGetValue(documentId, out var meta);
        return Task.FromResult(meta);
    }
}

/// <summary>
/// Maps a <see cref="DocumentMetadata"/> to the canonical source uri a citation
/// links back to. The default convention mirrors the MCP
/// <c>smartdocs://chunk/{id}</c> scheme used elsewhere in the book; production
/// can swap in a SharePoint / blob deep-link resolver.
/// </summary>
public static class SourceUriResolver
{
    /// <summary>Returns a stable <c>smartdocs://doc/{id}</c> uri for the document.</summary>
    public static string Resolve(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return $"smartdocs://doc/{metadata.Id}";
    }
}
