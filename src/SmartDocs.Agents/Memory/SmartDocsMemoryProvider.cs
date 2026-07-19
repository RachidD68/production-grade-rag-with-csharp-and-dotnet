using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Agents.Memory;

/// <summary>
/// A self-hosted, semantic long-term memory <see cref="AIContextProvider"/> built
/// on the Chapter-6 vector stack. This is the framework-native way to give an
/// agent durable memory: MAF runs the provider on the way <em>in</em> (recall) and
/// on the way <em>out</em> (remember) of every agent turn.
///
/// <list type="bullet">
///   <item><b>Recall</b> (<see cref="InvokingCoreAsync"/>) — embed the incoming
///   question, search the caller's memory partition, and return the top facts as a
///   system message MAF prepends to the request.</item>
///   <item><b>Remember</b> (<see cref="InvokedCoreAsync"/>) — after the turn,
///   distil one to three durable facts (via the summarizer, with a deterministic
///   fallback) and upsert them into the vector store.</item>
/// </list>
///
/// <para>
/// <b>Tenant isolation.</b> Every stored memory is keyed by <c>userId</c>: the
/// <c>userId</c> is written into the chunk's <see cref="DocumentMetadata.Author"/>
/// silo marker and into the chunk id, and recall filters on it. User B's similar
/// question can never surface user A's facts.
/// </para>
///
/// <para>
/// <b>Sessions, not fields.</b> A single provider instance is shared across every
/// session that uses the agent, so it holds <em>no</em> per-caller state in fields.
/// The <c>userId</c> (and the in-flight query passed from recall to remember) lives
/// in the <see cref="AgentSession.StateBag"/>. Call
/// <see cref="WithUserId(AgentSession, string)"/> on each fresh session to bind it
/// to a tenant before the first turn.
/// </para>
///
/// <para>
/// The managed alternative is <c>Mem0Provider</c> (see
/// <see cref="SmartDocsMemoryRegistration"/>); both are <see cref="AIContextProvider"/>s,
/// so swapping one for the other is a single line in
/// <see cref="ChatClientAgentOptions.AIContextProviders"/>.
/// </para>
/// </summary>
public sealed class SmartDocsMemoryProvider : AIContextProvider
{
    /// <summary>Session-state key under which the active tenant's id is stored.</summary>
    public const string UserIdStateKey = "smartdocs.memory.userId";

    /// <summary>Session-state key under which recall stashes the in-flight query for remember.</summary>
    private const string PendingQueryStateKey = "smartdocs.memory.pendingQuery";

    /// <summary>Synthetic document id all memory chunks share; the real partition key is the user id.</summary>
    private const string MemoryDocumentId = "smartdocs-memory";

    private readonly IVectorStore _store;
    private readonly IEmbeddingService _embeddings;
    private readonly IChatClient _summarizer;
    private readonly int _recallTopK;

    /// <summary>
    /// Create a provider over the supplied vector stack.
    /// </summary>
    /// <param name="store">The vector store memories are upserted into and recalled from (Ch 6 port).</param>
    /// <param name="embeddings">Embeds both the recall query and the facts being stored.</param>
    /// <param name="summarizer">Extracts durable facts from a completed turn. A deterministic fallback runs if it yields nothing usable.</param>
    /// <param name="recallTopK">How many memories to recall per turn.</param>
    public SmartDocsMemoryProvider(
        IVectorStore store,
        IEmbeddingService embeddings,
        IChatClient summarizer,
        int recallTopK = 3)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(summarizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recallTopK);

        _store = store;
        _embeddings = embeddings;
        _summarizer = summarizer;
        _recallTopK = recallTopK;
    }

    /// <summary>
    /// Binds <paramref name="session"/> to a tenant. Call this once on every fresh
    /// session before the first turn so recall and remember scope to
    /// <paramref name="userId"/>. The id is stored in the session's state bag, not
    /// on the (shared) provider.
    /// </summary>
    public static AgentSession WithUserId(AgentSession session, string userId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        session.StateBag.SetValue(UserIdStateKey, userId);
        return session;
    }

    /// <summary>
    /// Recall hook. Embeds the incoming question, searches the caller's memory
    /// partition, and returns the top facts as a single system message MAF prepends
    /// to the request. Returns an empty context when no tenant is bound or no
    /// memories match — the agent then runs ungrounded by memory, as it would
    /// without this provider.
    /// </summary>
    protected override async ValueTask<AIContext> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var empty = new AIContext();
        if (context.Session is null || !TryGetUserId(context.Session, out var userId))
        {
            return empty;
        }

        var query = LatestUserText(context.AIContext?.Messages);
        if (string.IsNullOrWhiteSpace(query))
        {
            return empty;
        }

        // Stash the query so the remember hook can pair it with the answer.
        context.Session.StateBag.SetValue(PendingQueryStateKey, query);

        var queryVector = await _embeddings.EmbedQueryAsync(query, cancellationToken).ConfigureAwait(false);

        // Over-fetch, then filter to this tenant in memory. The InMemoryVectorStore's
        // MetadataFilter is keyed on the fixed silo schema, so tenant scoping is applied
        // here against the user-id marker carried on each memory chunk — guaranteeing
        // user B never sees user A's facts regardless of the store's filter surface.
        var candidates = await _store
            .SearchAsync(queryVector, _recallTopK * 8, filter: null, cancellationToken)
            .ConfigureAwait(false);

        var facts = candidates
            .Where(r => string.Equals(UserIdOf(r.Chunk), userId, StringComparison.Ordinal))
            .Take(_recallTopK)
            .Select(r => r.Chunk.Text)
            .ToList();

        if (facts.Count == 0)
        {
            return empty;
        }

        var recalled = string.Join("\n", facts.Select(f => "- " + f));
        var message = new ChatMessage(
            ChatRole.System,
            "What you remember about this user from earlier conversations:\n" + recalled);

        return new AIContext { Messages = [message] };
    }

    /// <summary>
    /// Remember hook. After the turn completes, distils durable facts from the
    /// question and the agent's answer and upserts them into the caller's memory
    /// partition. No-ops when no tenant is bound or the turn faulted.
    /// </summary>
    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.InvokeException is not null ||
            context.Session is null ||
            !TryGetUserId(context.Session, out var userId))
        {
            return;
        }

        var question = PendingQuery(context.Session);
        var answer = LatestAssistantText(context.ResponseMessages);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return;
        }

        var facts = await ExtractFactsAsync(question, answer, cancellationToken).ConfigureAwait(false);
        if (facts.Count == 0)
        {
            return;
        }

        foreach (var fact in facts)
        {
            var embedded = await EmbedFactAsync(userId, fact, cancellationToken).ConfigureAwait(false);
            await _store.UpsertAsync([embedded], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Extracts one to three durable facts from a completed turn. Asks the
    /// summarizer first; if it returns nothing usable (e.g. an offline stub), falls
    /// back to a deterministic single-fact capture so memory still accrues.
    /// </summary>
    private async Task<IReadOnlyList<string>> ExtractFactsAsync(
        string? question,
        string answer,
        CancellationToken cancellationToken)
    {
        var prompt =
            "Extract up to three durable facts worth remembering about the user from this exchange. " +
            "Reply with one fact per line and nothing else. If there is nothing durable, reply with an empty line.\n\n" +
            $"User asked: {question}\nAssistant answered: {answer}";

        ChatResponse response;
        try
        {
            response = await _summarizer
                .GetResponseAsync([new ChatMessage(ChatRole.User, prompt)], options: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Memory extraction must never break the agent turn; degrade to the deterministic fallback.
        catch (Exception)
        {
            response = new ChatResponse();
        }
#pragma warning restore CA1031

        var facts = (response.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .Select(StripLeadingBullet)
            .Where(line => line.Length > 0)
            .Take(3)
            .ToList();

        if (facts.Count > 0)
        {
            return facts;
        }

        // Deterministic fallback: remember the answer itself as a single fact.
        var fallback = answer.Trim();
        return fallback.Length > 0 ? [fallback] : [];
    }

    private async Task<EmbeddedChunk> EmbedFactAsync(string userId, string fact, CancellationToken cancellationToken)
    {
        // Carry the userId on the chunk in two places: the chunk id (uniqueness +
        // scoping) and the Author marker (the field recall filters on).
        var chunkId = $"{MemoryDocumentId}#{userId}#{StableHash(fact).ToString(CultureInfo.InvariantCulture)}";
        var metadata = new DocumentMetadata(
            Id: MemoryDocumentId,
            Silo: "smartdocs-memory",
            Department: "Memory",
            Office: "n/a",
            ConfidentialityLevel: "Internal",
            DocumentType: "Memory",
            FiscalYear: 2026,
            Author: userId,
            LastModified: DateOnly.FromDateTime(DateTime.UtcNow),
            Title: "User memory");

        var chunk = new DocumentChunk(
            ChunkId: chunkId,
            DocumentId: MemoryDocumentId,
            ChunkIndex: 0,
            Text: fact,
            StartCharOffset: 0,
            EndCharOffset: fact.Length,
            Metadata: metadata);

        return await _embeddings.EmbedAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryGetUserId(AgentSession session, out string userId)
    {
        if (session.StateBag.TryGetValue<string>(UserIdStateKey, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            userId = value;
            return true;
        }

        userId = string.Empty;
        return false;
    }

    private static string? PendingQuery(AgentSession session) =>
        session.StateBag.TryGetValue<string>(PendingQueryStateKey, out var value) ? value : null;

    private static string UserIdOf(DocumentChunk chunk) => chunk.Metadata.Author;

    private static string? LatestUserText(IEnumerable<ChatMessage>? messages) =>
        messages?
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text)
            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private static string? LatestAssistantText(IEnumerable<ChatMessage>? messages) =>
        messages?
            .Where(m => m.Role == ChatRole.Assistant)
            .Select(m => m.Text)
            .LastOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private static string StripLeadingBullet(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return trimmed[2..].Trim();
        }
        return trimmed;
    }

    // FNV-1a (32-bit): a stable, process-independent hash so the same fact maps to
    // the same chunk id across runs (upsert stays idempotent), never string.GetHashCode.
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
}
