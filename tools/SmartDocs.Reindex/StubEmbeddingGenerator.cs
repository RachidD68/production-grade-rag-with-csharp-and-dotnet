using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace SmartDocs.Reindex;

/// <summary>
/// Deterministic offline <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> for
/// the reindex sketch. A stable FNV-1a hash buckets each word into a fixed-width
/// vector, then unit-normalizes — the same text always yields the same vector with
/// no model and no API key, so the whole migration runs in CI. A per-instance
/// <c>salt</c> lets the tool stand up two <em>different</em> embedding "models"
/// (old vs new) whose vector spaces differ, which is exactly what a real
/// embedding-model migration faces.
/// </summary>
internal sealed partial class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 64;

    private readonly string _modelId;
    private readonly uint _salt;

    public StubEmbeddingGenerator(string modelId, uint salt)
    {
        _modelId = modelId;
        _salt = salt;
    }

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
            embeddings.Add(new Embedding<float>(Encode(text)) { ModelId = _modelId });
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
        // Pure computation; nothing to dispose.
    }

    private float[] Encode(string text)
    {
        var vec = new float[Dimensions];
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            vec[(int)((StableHash(m.Value) ^ _salt) % Dimensions)] += 1f;
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

    // FNV-1a (32-bit) — stable across processes, unlike string.GetHashCode.
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
