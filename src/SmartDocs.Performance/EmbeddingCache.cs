using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;

namespace SmartDocs.Performance;

/// <summary>
/// Caches embedding vectors keyed by SHA-256 of the input text. Wraps any
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>; on cache miss
/// delegates to the inner generator and stores the resulting vector.
/// Used to skip the embedding API call for unchanged documents during
/// incremental indexing (Ch 22).
/// <para>
/// This cache is deliberately backed by <see cref="IMemoryCache"/> and is
/// therefore process-local — unlike the response and retrieval caches (Ch 21
/// Layers 1–2), which use <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// so a hit is shared across every instance. That is fine here because
/// embeddings are content-addressable (the SHA-256 key is a pure function of the
/// text) and cheap to recompute, so a per-process cache that occasionally
/// recomputes on a cold instance costs almost nothing.
/// </para>
/// </summary>
public sealed class EmbeddingCache : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _inner;
    private readonly IMemoryCache _cache;

    public EmbeddingCache(IEmbeddingGenerator<string, Embedding<float>> inner, IMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        _inner = inner;
        _cache = cache;
    }


    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var inputs = values.ToList();
        var results = new Embedding<float>?[inputs.Count];
        var missingIndices = new List<int>();

        for (int i = 0; i < inputs.Count; i++)
        {
            var key = Hash(inputs[i]);
            if (_cache.TryGetValue(key, out Embedding<float>? hit))
            {
                results[i] = hit;
            }
            else
            {
                missingIndices.Add(i);
            }
        }

        if (missingIndices.Count > 0)
        {
            var missingTexts = missingIndices.Select(i => inputs[i]).ToList();
            var generated = await _inner.GenerateAsync(missingTexts, options, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < missingIndices.Count; i++)
            {
                var idx = missingIndices[i];
                results[idx] = generated[i];
                _cache.Set(Hash(inputs[idx]), generated[i]);
            }
        }

        return new GeneratedEmbeddings<Embedding<float>>(results.Select(r => r!).ToList());
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => _inner.Dispose();

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return "emb:" + Convert.ToHexStringLower(bytes);
    }
}
