using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch07_IndexingStrategies;

/// <summary>
/// Deterministic, offline <see cref="IChatClient"/> for the harness. It never
/// reaches a model: it parses the passage out of the prompt the real
/// strategies send and synthesizes a plausible, keyword-bearing response so
/// recall stays meaningful run-to-run. Every call increments
/// <see cref="CallCount"/> so the comparison table can report index-time LLM
/// calls per strategy.
/// </summary>
internal sealed partial class CountingStubChatClient : IChatClient
{
    private int _callCount;

    /// <summary>Total number of <see cref="GetResponseAsync"/> calls so far.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>Reset the counter before timing a single strategy.</summary>
    public void Reset() => Interlocked.Exchange(ref _callCount, 0);

    public ChatClientMetadata Metadata { get; } = new("ch07-stub");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _callCount);
        var prompt = string.Join(
            "\n",
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        var passage = ExtractPassage(prompt);
        var reply = prompt.Contains("question", StringComparison.OrdinalIgnoreCase)
            ? BuildQuestions(passage, RequestedQuestionCount(prompt))
            : BuildSummary(passage);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        EnumerateAsync(messages, options, cancellationToken);

    private async IAsyncEnumerable<ChatResponseUpdate> EnumerateAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        // Nothing to dispose; the stub holds no unmanaged resources.
    }

    // The real strategies embed the passage after a "Passage:\n" marker.
    private static string ExtractPassage(string prompt)
    {
        const string Marker = "Passage:";
        var idx = prompt.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? prompt : prompt[(idx + Marker.Length)..].Trim();
    }

    // A "summary" the keeps the most distinctive words so it embeds near the chunk.
    private static string BuildSummary(string passage)
    {
        var first = FirstSentence(passage);
        var keywords = string.Join(' ', Keywords(passage).Take(6));
        return $"{first} Key topics: {keywords}.";
    }

    // N hypothetical questions, one per line. Each repeats the chunk's leading
    // sentence (which carries the topic) and rotates a distinct keyword into a
    // question-shaped phrasing, so the question embeddings sit near a user's
    // real query — the point of hypothetical-question indexing — while staying
    // differentiated from one another.
    private static string BuildQuestions(string passage, int count)
    {
        var keywords = Keywords(passage).ToList();
        var first = FirstSentence(passage).TrimEnd('.');
        if (keywords.Count == 0)
        {
            return first;
        }

        var lines = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var focus = keywords[i % keywords.Count];
            lines.Add($"What is the rule about {focus}? {first}.");
        }
        return string.Join('\n', lines);
    }

    private static int RequestedQuestionCount(string prompt)
    {
        var m = QuestionCountRegex().Match(prompt);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > 0 ? n : 3;
    }

    private static string FirstSentence(string text)
    {
        var parts = SentenceSplit().Split(text, 2);
        return parts.Length == 0 ? text.Trim() : parts[0].Trim();
    }

    // Distinctive content words: lower-cased, de-duplicated, stop-words removed.
    private static IEnumerable<string> Keywords(string text)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            var w = m.Value;
            if (w.Length >= 4 && !StopWords.Contains(w) && seen.Add(w))
            {
                yield return w;
            }
        }
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "this", "that", "with", "your", "must", "into", "than", "they", "them",
        "from", "have", "each", "every", "their", "after", "before", "within",
        "through", "during", "while", "when", "what", "which", "where", "about",
        "policy", "employee", "employees", "company", "requires", "allowed",
    };

    [GeneratedRegex(@"Generate\s+(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestionCountRegex();

    [GeneratedRegex(@"[a-z][a-z\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?<=[\.\!\?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceSplit();
}

/// <summary>
/// Deterministic bag-of-words <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>.
/// Hashes each word into a fixed-width vector and unit-normalizes, so the same
/// text always yields the same vector with no model or API key. This is the
/// offline default; pass a real generator (e.g. Ollama) to compare against a
/// production embedder.
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
            // Stable FNV-1a hash, NOT string.GetHashCode (which is randomized
            // per process), so the recall numbers this sample prints are
            // reproducible run to run.
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
