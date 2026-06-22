using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartDocs.Security.Abstractions;
using SmartDocs.Security.ContentSafety;

namespace SmartDocs.Security;

/// <summary>
/// DI helpers for wiring the SmartDocs injection-detection defences.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers an <see cref="IInjectionDetector"/>. By default the fully
    /// offline <see cref="HeuristicInjectionDetector"/> is used; set
    /// <paramref name="usePromptShields"/> to true to register the Azure
    /// Content Safety <b>Prompt Shields</b>-backed <see cref="PromptShieldDetector"/>
    /// instead — that path additionally requires a configured <see cref="HttpClient"/>
    /// (base address = Content Safety endpoint, plus a bearer/key auth header) to be
    /// supplied by the caller (the library stays credential-agnostic).
    /// </summary>
    public static IServiceCollection AddSmartDocsInjectionDetection(
        this IServiceCollection services,
        bool usePromptShields = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (usePromptShields)
        {
            services.TryAddSingleton<IInjectionDetector, PromptShieldDetector>();
        }
        else
        {
            services.TryAddSingleton<IInjectionDetector, HeuristicInjectionDetector>();
        }

        return services;
    }
}
