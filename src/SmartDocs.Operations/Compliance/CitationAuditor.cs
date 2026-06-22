using SmartDocs.Core.Abstractions;
using SmartDocs.Generation.Citations;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// One link in the audit chain: a citation the answer made, resolved back to the
/// exact chunk it pointed at, with that chunk's provenance signature recomputed
/// and verified. The id types are the repo's real <see cref="string"/> chunk /
/// document ids (e.g. <c>"hr-001#0"</c>), not GUIDs.
/// </summary>
/// <param name="CitationN">The 1-based source number the answer cited (<c>[Source N]</c>).</param>
/// <param name="ChunkId">Stable id of the cited chunk.</param>
/// <param name="ChunkText">The chunk's text as it was retrieved.</param>
/// <param name="DocumentId">Parent document id.</param>
/// <param name="SourceUri">Deep link back to the source document.</param>
/// <param name="ModifiedAt">The source document's last-modified date.</param>
/// <param name="ProvenanceSignature">The recomputed HMAC provenance signature (lowercase hex).</param>
/// <param name="SignatureValid">
/// <see langword="true"/> when the recomputed signature matches the one attached
/// at ingest — i.e. the chunk text was not altered between ingest and audit.
/// </param>
public sealed record ChainStep(
    int CitationN,
    string ChunkId,
    string ChunkText,
    string DocumentId,
    string SourceUri,
    DateTimeOffset ModifiedAt,
    string ProvenanceSignature,
    bool SignatureValid);

/// <summary>
/// The full, court-acceptable trace for one historical answer: the query id, the
/// answer text, and one <see cref="ChainStep"/> per citation, each carrying a
/// verified provenance signature.
/// </summary>
public sealed record AuditTrace(
    string QueryId,
    string Answer,
    IReadOnlyList<ChainStep> Chain);

/// <summary>
/// Traces a historical answer back through its citation chain to the source
/// chunks and proves each link. For a recorded query it pulls the
/// <see cref="AuditEntry"/> (query, answer, citations), resolves every cited
/// chunk by id, recomputes that chunk's provenance signature with the Ch 23
/// <see cref="IProvenanceSigner"/>, and compares it against the signature
/// attached at ingest. A mismatch (or a missing signature) flags the link as
/// tampered — the answer cited a chunk whose text no longer matches what the
/// trusted ingest path admitted.
/// </summary>
public sealed class CitationAuditor
{
    private readonly IAuditRecordStore _audit;
    private readonly IChunkLookup _chunks;
    private readonly IDocumentMetadataStore _documents;
    private readonly IProvenanceSigner _signer;

    public CitationAuditor(
        IAuditRecordStore audit,
        IChunkLookup chunks,
        IDocumentMetadataStore documents,
        IProvenanceSigner signer)
    {
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(signer);
        _audit = audit;
        _chunks = chunks;
        _documents = documents;
        _signer = signer;
    }

    /// <summary>
    /// Build the <see cref="AuditTrace"/> for the recorded answer to
    /// <paramref name="queryId"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No audit row exists for the query id.</exception>
    public async Task<AuditTrace> TraceAsync(string queryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryId);

        var entry = await _audit.GetAsync(queryId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"No audit record for query id '{queryId}'.");

        var steps = new List<ChainStep>(entry.Citations.Count);
        foreach (var citation in entry.Citations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            steps.Add(await BuildStepAsync(citation, cancellationToken).ConfigureAwait(false));
        }

        return new AuditTrace(queryId, entry.Response, steps);
    }

    private async Task<ChainStep> BuildStepAsync(Citation citation, CancellationToken cancellationToken)
    {
        var chunk = await _chunks.GetByIdAsync(citation.ChunkId, cancellationToken).ConfigureAwait(false);
        if (chunk is null)
        {
            // The cited chunk is gone (erased, or never existed). Record the gap
            // explicitly rather than dropping the link.
            return new ChainStep(
                CitationN: citation.SourceIndex,
                ChunkId: citation.ChunkId,
                ChunkText: string.Empty,
                DocumentId: citation.DocumentId,
                SourceUri: $"smartdocs://doc/{citation.DocumentId}",
                ModifiedAt: default,
                ProvenanceSignature: string.Empty,
                SignatureValid: false);
        }

        // Prefer the canonical document record for the modified-at / source uri,
        // falling back to the metadata denormalised onto the chunk.
        var doc = await _documents.GetAsync(chunk.DocumentId, cancellationToken).ConfigureAwait(false)
            ?? chunk.Metadata;
        var sourceUri = SourceUriResolver.Resolve(doc);
        var modifiedAt = new DateTimeOffset(doc.LastModified.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var recomputed = _signer.Sign(chunk);
        var valid = chunk.Provenance is { } attached
            && CryptographicEquals(attached, recomputed);

        return new ChainStep(
            CitationN: citation.SourceIndex,
            ChunkId: chunk.ChunkId,
            ChunkText: chunk.Text,
            DocumentId: chunk.DocumentId,
            SourceUri: sourceUri,
            ModifiedAt: modifiedAt,
            ProvenanceSignature: recomputed,
            SignatureValid: valid);
    }

    private static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
}
