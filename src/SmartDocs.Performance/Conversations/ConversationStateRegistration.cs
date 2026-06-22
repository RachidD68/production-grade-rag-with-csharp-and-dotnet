using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDocs.Core.Conversations;

namespace SmartDocs.Performance.Conversations;

/// <summary>
/// DI wiring for the conversation-state seam (Ch 25). Pick the tier that matches
/// the deployment: the in-memory store for the dev inner loop and tests, the
/// distributed (Redis) store for production behind a load balancer.
/// </summary>
public static class ConversationStateRegistration
{
    /// <summary>
    /// Register the process-local <see cref="InMemoryConversationStateStore"/> as
    /// the <see cref="IConversationStateStore"/>. The default for development and
    /// tests; not shared across instances and lost on restart.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddInMemoryConversationState(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IConversationStateStore>(
            sp => new InMemoryConversationStateStore(sp.GetService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Register the <see cref="DistributedConversationStateStore"/> (Redis hot
    /// store) as the <see cref="IConversationStateStore"/>. The host must also
    /// register an <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
    /// (e.g. <c>AddStackExchangeRedisCache(...)</c>); when Redis is not configured
    /// the host registers the in-memory distributed cache, so this still resolves
    /// and degrades to a single-instance store rather than failing.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddDistributedConversationState(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IConversationStateStore, DistributedConversationStateStore>();
        return services;
    }
}
