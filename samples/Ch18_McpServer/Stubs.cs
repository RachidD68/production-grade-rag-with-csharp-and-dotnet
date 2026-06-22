using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Documents;
using SmartDocs.Reranking;
using SmartDocs.Retrieval.Graph;

namespace RagInDotNet.Samples.Ch18_McpServer;

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Hashes each word into a fixed-width vector with a process-independent FNV-1a
/// hash (never <c>string.GetHashCode</c>, which is randomised per process) and
/// unit-normalises, so the server returns the same hits run to run with no model
/// or API key. Copied — deliberately, not referenced — from the Chapter 8 sample
/// so the MCP server stays offline and self-contained.
/// </summary>
internal sealed partial class BagOfWordsEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private const int Dimensions = 256;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var embeddings = new List<Embedding<float>>();
        foreach (var text in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings.Add(new Embedding<float>(Encode(text)) { ModelId = "bag-of-words-256" });
        }
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose; the generator is pure computation.
    }

    private static float[] Encode(string text)
    {
        var vec = new float[Dimensions];
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            vec[(int)(StableHash(m.Value) % Dimensions)] += 1f;
        }

        var mag = MathF.Sqrt(vec.Sum(v => v * v));
        if (mag > 0)
        {
            for (var i = 0; i < Dimensions; i++)
            {
                vec[i] /= mag;
            }
        }
        return vec;
    }

    // FNV-1a (32-bit) — a stable, process-independent string hash.
    private static uint StableHash(string s)
    {
        uint hash = 2166136261;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash;
    }

    [GeneratedRegex(@"[a-z0-9][a-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}

/// <summary>
/// No-op reranker — preserves the retriever's order and simply truncates to
/// topK. Keeps the MCP server offline (a real cross-encoder reranker would need
/// an ONNX model or an API key); the chapter swaps this for the Ch9 reranker in
/// production.
/// </summary>
internal sealed class PassThroughReranker : IReranker
{
    public string Implementation => "passthrough";

    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        int topK,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(candidates);
        return Task.FromResult<IReadOnlyList<RetrievalResult>>([.. candidates.Take(topK)]);
    }
}

/// <summary>
/// Minimal in-memory <see cref="IGraphStore"/> for the offline LazyGraphRAG path.
/// Holds a tiny entity set so <c>graph_search</c> has something to traverse; the
/// real Neo4j store ships in Chapter 13.
/// </summary>
internal sealed class StubGraphStore : IGraphStore
{
    private readonly IReadOnlyList<GraphEntity> _entities;

    public StubGraphStore(IReadOnlyList<GraphEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities = entities;
    }

    public Task EnsureSchemaExistsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpsertEntityAsync(GraphEntity entity, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpsertRelationAsync(GraphRelation relation, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object>>> QueryAsync(
        string query,
        IReadOnlyDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object>>>([]);

    public Task<IReadOnlyList<GraphEntity>> TraverseAsync(
        IEnumerable<string> entityNames,
        int maxHops,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityNames);
        var names = entityNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Return any seed entities that match, plus a couple of neighbours, so
        // the retriever has a non-empty subgraph to summarise offline.
        var matched = _entities
            .Where(e => names.Contains(e.Name) || names.Any(n => e.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matched.Count == 0 && _entities.Count > 0)
        {
            matched = [.. _entities.Take(2)];
        }
        return Task.FromResult<IReadOnlyList<GraphEntity>>(matched);
    }
}

/// <summary>
/// Offline <see cref="IChatClient"/> for the LazyGraphRAG subgraph summariser and
/// the entity extractor. It never calls a model: it echoes a deterministic
/// "summary" derived from the prompt's subgraph block (or an empty extraction),
/// so <c>graph_search</c> works with no API key.
/// </summary>
internal sealed class OfflineChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var text = string.Join(Environment.NewLine, messages.Select(m => m.Text));

        // Entity-extraction prompts expect a JSON object; reply with an empty,
        // well-formed extraction so the extractor's lenient parse succeeds and
        // the seed entity list comes from the deterministic graph instead.
        if (text.Contains("\"entities\"", StringComparison.Ordinal))
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant,
                    "{ \"entities\": [ { \"id\": \"policy\", \"type\": \"Document\", \"name\": \"policy\", \"properties\": {} } ], \"relations\": [] }")));
        }

        // Summarisation prompt — return a short deterministic paragraph.
        const string Summary =
            "The relevant SmartDocs entities are connected through policy and ownership relationships " +
            "that determine how documents, departments, and offices relate within the corpus.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Summary)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The offline MCP sample does not stream.");

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose.
    }
}
