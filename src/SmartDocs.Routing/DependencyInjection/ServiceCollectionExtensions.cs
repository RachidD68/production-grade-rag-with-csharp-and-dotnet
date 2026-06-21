using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Routing.Filtering;

namespace SmartDocs.Routing.DependencyInjection;

/// <summary>
/// DI registration extensions for the SmartDocs routing / self-query layer.
/// Lives in <c>SmartDocs.Routing</c> (not <c>SmartDocs.Core</c>) for the same
/// reason <c>AddSmartDocsIngestion</c> lives in <c>SmartDocs.Ingestion</c>: the
/// concrete types it wires up — <see cref="QueryConstructor"/> and
/// <see cref="SelfQueryRetriever"/> — are defined here, and <c>SmartDocs.Core</c>
/// must not depend on <c>SmartDocs.Routing</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the query-construction and self-query retrieval services:
    /// <see cref="QueryConstructor"/> (needs a registered <see cref="Microsoft.Extensions.AI.IChatClient"/>)
    /// and <see cref="SelfQueryRetriever"/> as an additional
    /// <see cref="IRetriever"/>. The self-query retriever also needs an
    /// <see cref="IEmbeddingService"/>, an <see cref="IVectorStore"/> (register a
    /// store via the <c>AddSmartDocs*VectorStore</c> extensions), and a
    /// <see cref="SecurityContext"/>.
    /// </summary>
    /// <remarks>
    /// The <see cref="SecurityContext"/> is intentionally <em>not</em> registered
    /// with a default here: it is auth-derived and must be supplied by the host
    /// (e.g. as a scoped service built from the authenticated principal). Call
    /// <see cref="AddSmartDocsRouting(IServiceCollection, SecurityContext)"/> to
    /// register a fixed context, or register your own <see cref="SecurityContext"/>
    /// before resolving <see cref="SelfQueryRetriever"/>.
    /// </remarks>
    /// <param name="services">The DI container being built.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<QueryConstructor>();
        // Additive registration: composes with any other IRetriever already
        // registered (dense, hybrid, ...). Resolve SelfQueryRetriever directly,
        // or enumerate IRetriever to pick by Strategy == "self-query".
        services.AddSingleton<SelfQueryRetriever>();
        services.AddSingleton<IRetriever>(sp => sp.GetRequiredService<SelfQueryRetriever>());

        // Query routing (Ch 12). The concrete routers are registered so a host can
        // resolve a specific strategy, and a MultiSourceRouter (rule-first, with an
        // LLM-classifier fallback) is wired as the default IQueryRouter. The
        // LLM-classifier and conversational rewriter need a registered IChatClient;
        // the embedding SemanticRouter needs a registered IEmbeddingService. Both
        // are host concerns supplied by the composition root.
        services.TryAddSingleton<RuleBasedRouter>();
        services.TryAddSingleton<LlmClassifierRouter>();
        services.TryAddSingleton<SemanticRouter>();
        services.TryAddSingleton<ConversationalQueryRewriter>();
        services.TryAddSingleton<IQueryRouter>(sp => new MultiSourceRouter(
            sp.GetRequiredService<RuleBasedRouter>(),
            sp.GetRequiredService<LlmClassifierRouter>()));

        return services;
    }

    /// <summary>
    /// Registers the routing services together with a fixed, host-supplied
    /// <see cref="SecurityContext"/>. Use this when the clearance scope is known
    /// at composition time (samples, single-tenant hosts); multi-tenant hosts
    /// should instead register a scoped <see cref="SecurityContext"/> per request
    /// and call <see cref="AddSmartDocsRouting(IServiceCollection)"/>.
    /// </summary>
    public static IServiceCollection AddSmartDocsRouting(
        this IServiceCollection services,
        SecurityContext securityContext)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(securityContext);

        services.TryAddSingleton(securityContext);
        return services.AddSmartDocsRouting();
    }
}
