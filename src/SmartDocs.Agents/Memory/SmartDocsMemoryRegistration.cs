using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Mem0;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;

namespace SmartDocs.Agents.Memory;

/// <summary>
/// Selects which long-term memory backend an agent uses.
/// </summary>
public enum MemoryBackend
{
    /// <summary>The self-hosted <see cref="SmartDocsMemoryProvider"/> over the Ch-6 vector stack.</summary>
    SelfHosted,

    /// <summary>The managed <c>Mem0Provider</c> from <c>Microsoft.Agents.AI.Mem0</c>.</summary>
    Mem0,
}

/// <summary>
/// Configuration for choosing and building a long-term memory
/// <see cref="AIContextProvider"/>. Binds cleanly from
/// <c>IConfiguration.GetSection("SmartDocs:Memory")</c>.
/// </summary>
public sealed class SmartDocsMemoryOptions
{
    /// <summary>Which backend to wire. Defaults to the self-hosted provider so the offline path needs no service.</summary>
    public MemoryBackend Backend { get; set; } = MemoryBackend.SelfHosted;

    /// <summary>Base address of the Mem0 service (only used when <see cref="Backend"/> is <see cref="MemoryBackend.Mem0"/>).</summary>
    public Uri? Mem0Endpoint { get; set; }

    /// <summary>Optional Mem0 application id (logical app partition above the user).</summary>
    public string? Mem0ApplicationId { get; set; }

    /// <summary>How many memories the self-hosted provider recalls per turn.</summary>
    public int RecallTopK { get; set; } = 3;
}

/// <summary>
/// Builds the long-term memory <see cref="AIContextProvider"/> the SmartDocs agent
/// attaches via <see cref="ChatClientAgentOptions.AIContextProviders"/>. Two backends
/// are wired behind one switch, so "the managed option is one switch away" is literally
/// true: flip <see cref="SmartDocsMemoryOptions.Backend"/> from
/// <see cref="MemoryBackend.SelfHosted"/> to <see cref="MemoryBackend.Mem0"/> and the
/// agent keeps the same shape.
///
/// <list type="bullet">
///   <item><see cref="MemoryBackend.SelfHosted"/> → <see cref="SmartDocsMemoryProvider"/>
///   over the Chapter-6 <see cref="IVectorStore"/> / <see cref="IEmbeddingService"/>.</item>
///   <item><see cref="MemoryBackend.Mem0"/> → <c>Mem0Provider</c> from
///   <c>Microsoft.Agents.AI.Mem0</c>, the managed long-term store.</item>
/// </list>
///
/// <para>
/// Both backends key memory by <c>userId</c>: the self-hosted provider via
/// <see cref="SmartDocsMemoryProvider.WithUserId(AgentSession, string)"/> on the session;
/// Mem0 via <c>Mem0ProviderOptions.UserId</c> on the provider it builds for the caller.
/// </para>
/// </summary>
public static class SmartDocsMemoryRegistration
{
    /// <summary>
    /// Builds the self-hosted <see cref="SmartDocsMemoryProvider"/>. The active tenant
    /// is bound per session with
    /// <see cref="SmartDocsMemoryProvider.WithUserId(AgentSession, string)"/>, so the
    /// returned instance is safe to share across users.
    /// </summary>
    public static AIContextProvider CreateSelfHosted(
        IVectorStore store,
        IEmbeddingService embeddings,
        IChatClient summarizer,
        int recallTopK = 3)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(summarizer);

        return new SmartDocsMemoryProvider(store, embeddings, summarizer, recallTopK);
    }

    /// <summary>
    /// Builds the managed <c>Mem0Provider</c> for a specific <paramref name="userId"/>.
    /// Mem0 keys memory by user on the provider itself, so this returns a per-tenant
    /// provider (unlike the self-hosted one, which scopes per session).
    /// </summary>
    /// <param name="httpClient">An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> points at the Mem0 service.</param>
    /// <param name="userId">The tenant whose memories this provider reads and writes.</param>
    /// <param name="applicationId">Optional logical application partition above the user.</param>
    public static AIContextProvider CreateMem0(HttpClient httpClient, string userId, string? applicationId = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var options = new Mem0ProviderOptions
        {
            UserId = userId,
            ApplicationId = applicationId,
        };
        return new Mem0Provider(httpClient, options);
    }

    /// <summary>
    /// Builds the memory provider chosen by <paramref name="options"/> for the given
    /// <paramref name="userId"/>. The self-hosted branch binds the tenant on
    /// <paramref name="session"/>; the Mem0 branch builds a per-tenant provider against
    /// <paramref name="httpClient"/>. Throws when the Mem0 branch is selected without an
    /// <see cref="HttpClient"/> — fail fast rather than ship a silently inert provider.
    /// </summary>
    public static AIContextProvider Create(
        SmartDocsMemoryOptions options,
        string userId,
        AgentSession session,
        IVectorStore store,
        IEmbeddingService embeddings,
        IChatClient summarizer,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(session);

        switch (options.Backend)
        {
            case MemoryBackend.Mem0:
                if (httpClient is null)
                {
                    throw new InvalidOperationException(
                        "MemoryBackend.Mem0 requires an HttpClient pointing at the Mem0 service. " +
                        "Pass one (configured with SmartDocsMemoryOptions.Mem0Endpoint) or use MemoryBackend.SelfHosted.");
                }
                return CreateMem0(httpClient, userId, options.Mem0ApplicationId);

            case MemoryBackend.SelfHosted:
            default:
                SmartDocsMemoryProvider.WithUserId(session, userId);
                return CreateSelfHosted(store, embeddings, summarizer, options.RecallTopK);
        }
    }
}
