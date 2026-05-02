using System.Globalization;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Retrieval.VectorStores;

/// <summary>
/// Qdrant-backed <see cref="IVectorStore"/>. Uses the official
/// <c>Qdrant.Client</c> gRPC client.
/// </summary>
/// <remarks>
/// Each <see cref="EmbeddedChunk"/> becomes a Qdrant point keyed by a
/// stable Guid derived from the chunk's <see cref="DocumentChunk.ChunkId"/>.
/// The chunk text + full <see cref="DocumentMetadata"/> ride in the point's
/// payload so a search round-trip can reconstruct a complete
/// <see cref="DocumentChunk"/> without a separate document store.
/// </remarks>
public sealed class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;
    private readonly int _vectorSize;
    private readonly Distance _distance;

    public QdrantVectorStore(
        QdrantClient client,
        string collectionName,
        int vectorSize,
        Distance distance = Distance.Cosine)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vectorSize);

        _client = client;
        CollectionName = collectionName;
        _vectorSize = vectorSize;
        _distance = distance;
    }

    public string CollectionName { get; }

    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _client.CollectionExistsAsync(CollectionName, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return;
        }
        await _client.CreateCollectionAsync(
            CollectionName,
            new VectorParams { Size = (ulong)_vectorSize, Distance = _distance },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var points = chunks.Select(BuildPoint).ToList();
        if (points.Count == 0)
        {
            return;
        }
        await _client.UpsertAsync(CollectionName, points, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        var hits = await _client.SearchAsync(
            CollectionName,
            queryVector.ToArray(),
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return [.. hits.Select(h => new RetrievalResult(BuildChunk(h.Payload), h.Score))];
    }

    public async Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        var ids = chunkIds.Select(ToPointId).ToList();
        if (ids.Count == 0)
        {
            return;
        }
        await _client.DeleteAsync(CollectionName, ids, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static PointStruct BuildPoint(EmbeddedChunk e)
    {
        var p = new PointStruct
        {
            Id = ToPointId(e.Chunk.ChunkId),
            Vectors = e.Vector.ToArray(),
        };
        var m = e.Chunk.Metadata;
        p.Payload["chunk_id"] = e.Chunk.ChunkId;
        p.Payload["document_id"] = e.Chunk.DocumentId;
        p.Payload["chunk_index"] = e.Chunk.ChunkIndex;
        p.Payload["text"] = e.Chunk.Text;
        p.Payload["start_offset"] = e.Chunk.StartCharOffset;
        p.Payload["end_offset"] = e.Chunk.EndCharOffset;
        p.Payload["silo"] = m.Silo;
        p.Payload["department"] = m.Department;
        p.Payload["office"] = m.Office;
        p.Payload["confidentiality"] = m.ConfidentialityLevel;
        p.Payload["document_type"] = m.DocumentType;
        p.Payload["fiscal_year"] = m.FiscalYear;
        p.Payload["author"] = m.Author;
        p.Payload["last_modified"] = m.LastModified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        p.Payload["title"] = m.Title;
        return p;
    }

    private static DocumentChunk BuildChunk(Google.Protobuf.Collections.MapField<string, Value> payload)
    {
        var meta = new DocumentMetadata(
            Id: payload["document_id"].StringValue,
            Silo: payload["silo"].StringValue,
            Department: payload["department"].StringValue,
            Office: payload["office"].StringValue,
            ConfidentialityLevel: payload["confidentiality"].StringValue,
            DocumentType: payload["document_type"].StringValue,
            FiscalYear: (int)payload["fiscal_year"].IntegerValue,
            Author: payload["author"].StringValue,
            LastModified: DateOnly.ParseExact(
                payload["last_modified"].StringValue, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Title: payload["title"].StringValue);

        return new DocumentChunk(
            ChunkId: payload["chunk_id"].StringValue,
            DocumentId: meta.Id,
            ChunkIndex: (int)payload["chunk_index"].IntegerValue,
            Text: payload["text"].StringValue,
            StartCharOffset: (int)payload["start_offset"].IntegerValue,
            EndCharOffset: (int)payload["end_offset"].IntegerValue,
            Metadata: meta);
    }

    /// <summary>Stable Guid v5-style derivation from a string ChunkId.</summary>
    private static PointId ToPointId(string chunkId)
    {
        // Qdrant accepts UUID or u64. We hash the chunkId into a deterministic Guid.
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(chunkId));
        var guid = new Guid(bytes.AsSpan(0, 16));
        return new PointId { Uuid = guid.ToString() };
    }
}
