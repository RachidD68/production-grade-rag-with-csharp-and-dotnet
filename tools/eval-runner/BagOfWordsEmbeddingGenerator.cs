using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace RagInDotNet.Tools.EvalRunner;

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>:
/// a process-independent FNV-1a hash buckets each word into a fixed-width vector,
/// then unit-normalises. The same text always yields the same vector with no
/// model and no API key, so the metrics this runner prints — and the confidence
/// intervals derived from them — reproduce exactly in CI.
///
/// <para>
/// Copied (not referenced) from the Chapter 8 retriever-eval sample so the eval
/// runner stays self-contained and offline. The critical detail is the stable
/// FNV-1a hash; never <c>string.GetHashCode</c>, which is randomised per process.
/// </para>
/// </summary>
internal sealed partial class BagOfWordsEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 256;

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
