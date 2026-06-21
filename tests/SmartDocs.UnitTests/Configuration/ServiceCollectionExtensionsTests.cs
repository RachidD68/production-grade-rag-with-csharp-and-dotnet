using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.DependencyInjection;
using SmartDocs.Ingestion.Chunking;
using SmartDocs.Ingestion.DependencyInjection;

namespace SmartDocs.UnitTests.Configuration;

/// <summary>
/// Covers the Chapter 4 "wiring chunking into the SmartDocs pipeline" lesson:
/// <see cref="SmartDocs.Ingestion.DependencyInjection.ServiceCollectionExtensions.AddSmartDocsIngestion"/>
/// resolves the right <see cref="IChunker"/> based on the
/// <c>SmartDocs:Ingestion</c> toggle.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Flag_off_resolves_RecursiveCharacterChunker()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
            ["SmartDocs:Ingestion:UseContextualRetrieval"] = "false",
            ["SmartDocs:Ingestion:MaxChunkSize"] = "800",
        });

        var chunker = sp.GetRequiredService<IChunker>();

        var recursive = Assert.IsType<RecursiveCharacterChunker>(chunker);
        Assert.Equal(800, recursive.MaxChunkSize);
        Assert.Equal("recursive", recursive.Strategy);
    }

    [Fact]
    public void Flag_on_resolves_ContextualChunker()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
            ["SmartDocs:Ingestion:UseContextualRetrieval"] = "true",
            ["SmartDocs:Ingestion:MaxChunkSize"] = "800",
        });

        var chunker = sp.GetRequiredService<IChunker>();

        var contextual = Assert.IsType<ContextualChunker>(chunker);
        Assert.Equal("contextual(recursive)", contextual.Strategy);
    }

    [Fact]
    public void MaxChunkSize_flows_through_to_the_recursive_budget()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
            ["SmartDocs:Ingestion:MaxChunkSize"] = "1200",
        });

        var recursive = Assert.IsType<RecursiveCharacterChunker>(sp.GetRequiredService<IChunker>());
        Assert.Equal(1200, recursive.MaxChunkSize);
    }

    [Fact]
    public void Default_when_section_absent_is_non_contextual_recursive_800()
    {
        var sp = BuildProvider(new Dictionary<string, string?>
        {
            ["SmartDocs:Llm:Provider"] = "Ollama",
        });

        var recursive = Assert.IsType<RecursiveCharacterChunker>(sp.GetRequiredService<IChunker>());
        Assert.Equal(800, recursive.MaxChunkSize);
    }

    private static ServiceProvider BuildProvider(IDictionary<string, string?> config)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        // Core registers IChatClient (needed when the contextual flag is on);
        // Ingestion registers the IChunker the toggle selects.
        services.AddSmartDocsCore(configuration);
        services.AddSmartDocsIngestion(configuration);
        return services.BuildServiceProvider();
    }
}
