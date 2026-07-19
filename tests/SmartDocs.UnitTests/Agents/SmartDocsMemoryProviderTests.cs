using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Agents;
using SmartDocs.Agents.Memory;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.Agents;

/// <summary>
/// Tests for the self-hosted <see cref="SmartDocsMemoryProvider"/>: a recall/remember
/// round-trip and, critically, tenant isolation — user B's similar query must never
/// surface user A's facts. Fully offline: an FNV bag-of-words embedder, the in-memory
/// vector store, and a deterministic stub summarizer.
/// </summary>
public sealed partial class SmartDocsMemoryProviderTests
{
    private const int Dimensions = 256;

    [Fact]
    public async Task Recall_round_trips_a_remembered_fact_for_the_same_user()
    {
        var store = new InMemoryVectorStore("mem-roundtrip");
        await store.EnsureCollectionExistsAsync();
        var embeddings = BuildEmbeddingService();
        var summarizer = new ScriptedChatClient(_ => "The user works in the Montreal office.");

        // Turn 1 (user A): the agent answers, then the provider remembers a fact.
        await RememberAsync(store, embeddings, summarizer, "user-A", "Where am I based?");

        // Turn 2 (user A, fresh session): recall must surface the stored fact.
        var recall = await RecallAsync(store, embeddings, summarizer, "user-A", "Which office am I in?");

        Assert.Contains("Montreal", recall, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_is_scoped_by_userId_and_never_leaks_across_tenants()
    {
        var store = new InMemoryVectorStore("mem-isolation");
        await store.EnsureCollectionExistsAsync();
        var embeddings = BuildEmbeddingService();
        var summarizer = new ScriptedChatClient(_ => "The user's favorite project is codenamed Falcon.");

        // User A remembers a fact.
        await RememberAsync(store, embeddings, summarizer, "user-A", "My favorite project is Falcon.");

        // User B asks an almost identical question — must NOT see user A's fact.
        var recallB = await RecallAsync(store, embeddings, summarizer, "user-B", "What is my favorite project?");
        Assert.DoesNotContain("Falcon", recallB, StringComparison.Ordinal);

        // Sanity: user A on a fresh session DOES see it, proving the fact was stored.
        var recallA = await RecallAsync(store, embeddings, summarizer, "user-A", "What is my favorite project?");
        Assert.Contains("Falcon", recallA, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_no_ops_when_no_tenant_is_bound()
    {
        var store = new InMemoryVectorStore("mem-no-tenant");
        await store.EnsureCollectionExistsAsync();
        var embeddings = BuildEmbeddingService();
        var provider = new SmartDocsMemoryProvider(store, embeddings, new ScriptedChatClient(_ => "fact"));

        var chat = new CapturingChatClient();
        var agent = SmartDocsAgent.CreateWithMemory(chat, provider, new ConstantRetriever([]));

        // No WithUserId call — the provider must not recall a memory system message or throw.
        var session = await agent.CreateSessionAsync();
        await agent.RunAsync("anything", session, options: null);

        Assert.DoesNotContain(
            chat.SystemMessages,
            s => s.Contains("remember about this user", StringComparison.Ordinal));
    }

    // Runs one turn that lets the provider remember a fact for the user.
    private static async Task RememberAsync(
        InMemoryVectorStore store,
        EmbeddingService embeddings,
        IChatClient summarizer,
        string userId,
        string question)
    {
        var provider = new SmartDocsMemoryProvider(store, embeddings, summarizer);
        var agent = SmartDocsAgent.CreateWithMemory(
            new CapturingChatClient(), provider, new ConstantRetriever([]));
        var session = await agent.CreateSessionAsync();
        SmartDocsMemoryProvider.WithUserId(session, userId);
        await agent.RunAsync(question, session, options: null);
    }

    // Runs one turn and returns the recall system message the provider prepended (or "").
    private static async Task<string> RecallAsync(
        InMemoryVectorStore store,
        EmbeddingService embeddings,
        IChatClient summarizer,
        string userId,
        string question)
    {
        var provider = new SmartDocsMemoryProvider(store, embeddings, summarizer);
        var chat = new CapturingChatClient();
        var agent = SmartDocsAgent.CreateWithMemory(chat, provider, new ConstantRetriever([]));
        var session = await agent.CreateSessionAsync();
        SmartDocsMemoryProvider.WithUserId(session, userId);
        await agent.RunAsync(question, session, options: null);

        return string.Join("\n", chat.SystemMessages);
    }

    private static EmbeddingService BuildEmbeddingService() =>
        new(
            new StubEmbeddingGenerator(FnvBagOfWords),
            "fnv-256",
            Dimensions,
            NullLogger<EmbeddingService>.Instance);

    // FNV bag-of-words so semantically overlapping text yields nearby vectors,
    // letting recall match a stored fact to a related query — deterministic, offline.
    private static float[] FnvBagOfWords(string text)
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

    // Captures every system message it is asked to respond to, so tests can assert on
    // the recall context the provider prepended.
    private sealed class CapturingChatClient : IChatClient
    {
        public List<string> SystemMessages { get; } = [];

        public ChatClientMetadata Metadata { get; } = new("capturing");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(messages);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }

        private void Capture(IEnumerable<ChatMessage> messages)
        {
            foreach (var m in messages.Where(m => m.Role == ChatRole.System))
            {
                if (!string.IsNullOrWhiteSpace(m.Text))
                {
                    SystemMessages.Add(m.Text);
                }
            }
        }
    }

    // Deterministic summarizer returning a scripted fact line.
    private sealed class ScriptedChatClient : IChatClient
    {
        private readonly Func<string, string> _respond;

        public ScriptedChatClient(Func<string, string> respond) { _respond = respond; }

        public ChatClientMetadata Metadata { get; } = new("scripted");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var prompt = string.Join("\n", messages.Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _respond(prompt))));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var prompt = string.Join("\n", messages.Select(m => m.Text));
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _respond(prompt));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class ConstantRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _r;
        public ConstantRetriever(IReadOnlyList<RetrievalResult> r) { _r = r; }
        public string Strategy => "stub";
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string q, int k, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([.. _r.Take(k)]);
    }
}
