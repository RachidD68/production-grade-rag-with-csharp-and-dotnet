using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Retrieval.VectorStores;
using SmartDocs.Routing.DependencyInjection;
using SmartDocs.Routing.Filtering;

namespace SmartDocs.UnitTests.Routing;

/// <summary>
/// Lifetime guarantees for <c>AddSmartDocsRouting</c>.
/// </summary>
/// <remarks>
/// <see cref="SelfQueryRetriever"/> takes a <see cref="SecurityContext"/>, which the
/// extension's own remarks tell multi-tenant hosts to register per request. It was
/// registered as a singleton, so under a scoped context it either failed scope
/// validation or -- worse, with validation off -- captured the FIRST request's
/// clearance and served every later caller under it. These tests pin the retriever
/// to a scoped lifetime so that cannot regress.
/// </remarks>
public sealed class RoutingRegistrationTests
{
    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(new StubChatClient(_ => "{}"));
        services.AddSingleton<IVectorStore>(new InMemoryVectorStore("test"));
        services.AddSingleton<IEmbeddingService>(new EmbeddingService(
            new StubEmbeddingGenerator(_ => [1f, 0f]),
            "stub-model",
            1,
            NullLogger<EmbeddingService>.Instance));
        return services;
    }

    [Fact]
    public void Container_validates_with_a_scoped_SecurityContext()
    {
        var services = BaseServices();
        // The multi-tenant shape the extension's remarks recommend.
        services.AddScoped(_ => new SecurityContext("Internal"));
        services.AddSmartDocsRouting();

        // ValidateOnBuild + ValidateScopes is what catches a captive dependency.
        // With a singleton retriever over a scoped context this throws.
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<SelfQueryRetriever>());
    }

    [Fact]
    public void Each_scope_gets_its_own_retriever_and_clearance()
    {
        var services = BaseServices();
        var clearances = new Queue<string>(["Public", "Confidential"]);
        services.AddScoped(_ => new SecurityContext(clearances.Dequeue()));
        services.AddSmartDocsRouting();

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<SelfQueryRetriever>();
        var b = second.ServiceProvider.GetRequiredService<SelfQueryRetriever>();

        // Distinct instances: the second caller is not served the first
        // caller's captured clearance.
        Assert.NotSame(a, b);
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<SecurityContext>(),
            second.ServiceProvider.GetRequiredService<SecurityContext>());
        Assert.Equal("Public", first.ServiceProvider.GetRequiredService<SecurityContext>().ClearanceLevel);
        Assert.Equal("Confidential", second.ServiceProvider.GetRequiredService<SecurityContext>().ClearanceLevel);
    }

    [Fact]
    public void Fixed_context_overload_still_works_for_single_tenant_hosts()
    {
        var services = BaseServices();
        services.AddSmartDocsRouting(new SecurityContext("Internal", Office: "Paris"));

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();
        var retriever = scope.ServiceProvider.GetRequiredService<SelfQueryRetriever>();

        Assert.NotNull(retriever);
        Assert.Equal("Paris", scope.ServiceProvider.GetRequiredService<SecurityContext>().Office);
    }
}
