using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch16_VectorlessRetrieval;

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Hashes each word into a fixed-width vector with a process-independent FNV-1a
/// hash (never <c>string.GetHashCode</c>, which is randomized per process) and
/// unit-normalizes, so the recall numbers this sample prints reproduce run to run
/// with no model or API key. Copied — deliberately, not referenced — from the
/// Chapter 8 sample so the two harnesses stay independent.
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
/// Offline, deterministic stand-in for the identifier-extraction
/// <see cref="IChatClient"/> the structural retriever uses. Instead of calling a
/// model, it regex-extracts a formal identifier (<c>Article N</c>, <c>§N</c>)
/// from the query and replies with the same JSON shape M.E.AI would parse into
/// <c>StructuralQuery</c> — so the whole sample is reproducible with no key.
/// </summary>
internal sealed partial class OfflineIdentifierChatClient : IChatClient
{
    [GeneratedRegex(@"Article\s+(\d+)|§\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var text = string.Join(
            Environment.NewLine,
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        // Look only at the actual question (after the last "Question:" marker) so
        // the prompt's own example identifiers ("Art17", "§17") never leak in.
        var marker = text.LastIndexOf("Question:", StringComparison.Ordinal);
        var question = marker >= 0 ? text[(marker + "Question:".Length)..] : text;

        var m = IdentifierRegex().Match(question);
        string json;
        if (m.Success)
        {
            var number = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            json = $"{{ \"explicitId\": \"Art{number}\", \"topic\": null }}";
        }
        else
        {
            json = "{ \"explicitId\": null, \"topic\": \"general\" }";
        }
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The offline sample does not stream.");

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
