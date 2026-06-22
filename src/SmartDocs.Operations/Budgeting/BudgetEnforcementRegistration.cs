using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDocs.Generation;

namespace SmartDocs.Operations.Budgeting;

/// <summary>
/// DI wiring for the Ch 25 budget governor. Registers the
/// <see cref="TenantSpendLedger"/> and decorates the already-registered
/// <see cref="IRagPipeline"/> with the <see cref="BudgetEnforcingPipeline"/> so
/// enforcement is transparent to call sites — they still depend on
/// <see cref="IRagPipeline"/>, but the governor now fronts the real pipeline.
/// </summary>
public static class BudgetEnforcementRegistration
{
    /// <summary>
    /// Add the budget governor in front of the registered
    /// <see cref="IRagPipeline"/>. Call after the inner pipeline (and any cache
    /// decorator) is registered; the governor becomes the outermost layer so a
    /// refusal short-circuits everything below it, including the cache.
    /// </summary>
    /// <param name="services">The DI service collection.</param>
    /// <param name="policy">The budget policy. Defaults to <see cref="BudgetPolicy.Default"/>.</param>
    /// <param name="window">
    /// The ledger's rolling window. Defaults to one hour (the quota is "per this window").
    /// </param>
    /// <param name="tenantAccessor">
    /// Resolves the current tenant id. Defaults to a single <c>"default"</c> tenant.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddBudgetEnforcement(
        this IServiceCollection services,
        BudgetPolicy? policy = null,
        TimeSpan? window = null,
        Func<string>? tenantAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var effectivePolicy = policy ?? BudgetPolicy.Default;
        var effectiveWindow = window ?? TimeSpan.FromHours(1);

        services.TryAddSingleton(sp =>
            new TenantSpendLedger(effectiveWindow, sp.GetService<TimeProvider>()));

        // Decorate IRagPipeline manually (no Scrutor dependency in this leaf
        // package): capture the last registered IRagPipeline factory and rebind
        // the service to the governor wrapping it, so the governor becomes the
        // outermost layer over whatever was registered (the real pipeline, or a
        // cache decorator over it). A refusal short-circuits everything below.
        var innerDescriptor = services.LastOrDefault(d => d.ServiceType == typeof(IRagPipeline))
            ?? throw new InvalidOperationException(
                "AddBudgetEnforcement requires an IRagPipeline to already be registered.");
        services.Remove(innerDescriptor);

        services.Add(ServiceDescriptor.Describe(
            typeof(IRagPipeline),
            sp => new BudgetEnforcingPipeline(
                (IRagPipeline)CreateInner(innerDescriptor, sp),
                effectivePolicy,
                sp.GetRequiredService<TenantSpendLedger>(),
                tenantAccessor,
                timeProvider: sp.GetService<TimeProvider>()),
            innerDescriptor.Lifetime));

        return services;
    }

    private static object CreateInner(ServiceDescriptor descriptor, IServiceProvider sp)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return descriptor.ImplementationFactory(sp);
        }

        // Registered by concrete type (e.g. AddSingleton<IRagPipeline, RagPipeline>()).
        return ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!);
    }
}
