using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch19_MultiAgentOrchestration;

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Hashes each word into a fixed-width vector and unit-normalises, so the same
/// text always yields the same vector with no model or API key. This keeps the
/// sample fully offline and reproducible run to run.
///
/// <para>
/// Copied — deliberately, not referenced — from the Chapter 8 sample so the
/// orchestration sample stays self-contained. The critical detail is
/// <see cref="StableHash"/>: a process-independent FNV-1a hash, never
/// <c>string.GetHashCode</c> (which is randomised per process and would make
/// retrieval non-deterministic).
/// </para>
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
