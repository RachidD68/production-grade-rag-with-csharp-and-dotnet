using Microsoft.Extensions.AI;

namespace SmartDocs.UnitTests;

/// <summary>
/// Deterministic <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> that
/// maps each input string to a vector via a caller-supplied function.
/// Used by the Chapter-1 Hello-World tests to guarantee the cosine-ranker
/// picks the documented chunk without touching a real provider.
/// </summary>
internal sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Func<string, float[]> _embed;

    public StubEmbeddingGenerator(Func<string, float[]> embed)
    {
        _embed = embed;
    }

    public EmbeddingGeneratorMetadata Metadata { get; } = new("stub");

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = values.Select(v => new Embedding<float>(_embed(v))).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }
}
