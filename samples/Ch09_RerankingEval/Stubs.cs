using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using SmartDocs.Reranking;

namespace RagInDotNet.Samples.Ch09_RerankingEval;

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Hashes each word into a fixed-width vector and unit-normalises, so the same
/// text always yields the same vector with no model or API key — making the
/// metrics this sample prints reproducible run to run.
///
/// <para>
/// Copied — deliberately, not referenced — from the Chapter 8 sample so the two
/// harnesses stay independent. The critical detail is <see cref="StableHash"/>:
/// a process-independent FNV-1a hash, never <c>string.GetHashCode</c> (which is
/// randomised per process and would make the numbers non-deterministic).
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
            // Stable FNV-1a hash, NOT string.GetHashCode (which is randomised
            // per process), so the numbers this sample prints reproduce run to run.
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
/// A deterministic <see cref="IChatClient"/> that stands in for the LLM judge
/// behind <see cref="LlmRerank"/>. It parses the <c>Question:</c> and
/// <c>Passage:</c> blocks out of the rerank prompt and returns a relevance
/// number derived from lexical overlap between the two, so vacation-policy
/// passages score high for vacation queries — with no model, key, or network,
/// and identical output on every run.
///
/// <para>
/// Overlap is the share of distinct query words (minus a few stopwords) that
/// also appear in the passage, which makes a topically-matched passage clearly
/// outscore an incidental keyword hit. The result is formatted as a bare
/// <c>0.000</c>..<c>1.000</c> string, exactly what the reranker's parser expects.
/// </para>
/// </summary>
internal sealed partial class LexicalOverlapChatClient : IChatClient
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "for", "to", "of", "in", "on", "is", "are",
        "do", "i", "my", "can", "what", "how", "when", "where", "much", "many",
        "get", "need", "must", "be", "after", "with", "that", "this", "it",
    };

    public ChatClientMetadata Metadata { get; } = new("lexical-overlap-stub");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var prompt = string.Join(
            Environment.NewLine,
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));

        var score = ScorePrompt(prompt);
        var reply = score.ToString("0.000", CultureInfo.InvariantCulture);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return EnumerateAsync(messages);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> EnumerateAsync(IEnumerable<ChatMessage> messages)
    {
        var response = await GetResponseAsync(messages).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose; the client is pure computation.
    }

    private static double ScorePrompt(string prompt)
    {
        // The LlmRerank prompt ends with "Question: {q}\n\nPassage: {text}".
        var questionIdx = prompt.IndexOf("Question:", StringComparison.Ordinal);
        var passageIdx = prompt.IndexOf("Passage:", StringComparison.Ordinal);
        if (questionIdx < 0 || passageIdx < 0 || passageIdx <= questionIdx)
        {
            return 0.0;
        }

        var question = prompt.Substring(questionIdx + "Question:".Length, passageIdx - questionIdx - "Question:".Length);
        var passage = prompt[(passageIdx + "Passage:".Length)..];

        var queryWords = Tokenize(question);
        if (queryWords.Count == 0)
        {
            return 0.0;
        }
        var passageWords = Tokenize(passage);

        int overlap = queryWords.Count(passageWords.Contains);
        return (double)overlap / queryWords.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            if (!Stopwords.Contains(m.Value))
            {
                set.Add(m.Value);
            }
        }
        return set;
    }

    [GeneratedRegex(@"[a-z0-9][a-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
