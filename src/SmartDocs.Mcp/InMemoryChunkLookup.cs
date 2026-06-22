using System.Collections.Concurrent;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Mcp;

/// <summary>
/// Dictionary-backed <see cref="IChunkLookup"/> keyed by
/// <see cref="DocumentChunk.ChunkId"/>. Use for the samples, unit tests, and
/// the development inner loop where a real document store would be overkill —
/// production binds <see cref="IChunkLookup"/> to the vector / document store
/// instead.
/// </summary>
public sealed class InMemoryChunkLookup : IChunkLookup
{
    private readonly ConcurrentDictionary<string, DocumentChunk> _byId;

    /// <summary>Create an empty lookup; add chunks with <see cref="Add"/>.</summary>
    public InMemoryChunkLookup()
    {
        _byId = new ConcurrentDictionary<string, DocumentChunk>(StringComparer.Ordinal);
    }

    /// <summary>Create a lookup pre-populated from <paramref name="chunks"/>.</summary>
    public InMemoryChunkLookup(IEnumerable<DocumentChunk> chunks)
        : this()
    {
        ArgumentNullException.ThrowIfNull(chunks);
        foreach (var chunk in chunks)
        {
            Add(chunk);
        }
    }

    /// <summary>Insert or replace a chunk in the lookup.</summary>
    public void Add(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _byId[chunk.ChunkId] = chunk;
    }

    /// <inheritdoc />
    public Task<DocumentChunk?> GetByIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_byId.TryGetValue(chunkId, out var chunk) ? chunk : null);
    }
}
