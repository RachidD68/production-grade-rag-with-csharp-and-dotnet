using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Mcp;
using SmartDocs.Reranking;
using SmartDocs.Retrieval;
using SmartDocs.Retrieval.Graph;
using SmartDocs.Retrieval.VectorStores;

namespace RagInDotNet.Samples.Ch18_McpServer;

/// <summary>
/// Registers the offline SmartDocs retrieval pipeline the MCP tools depend on:
/// the FNV bag-of-words embedder, an in-memory vector store seeded from
/// <see cref="Corpus"/>, a base <see cref="DenseRetriever"/>, a no-op reranker,
/// a stub LazyGraphRAG retriever, and the by-id <see cref="IChunkLookup"/>. The
/// same registrations back the stdio and HTTP hosts so dev/prod stay in parity —
/// only the transport and the <see cref="ITenantContext"/> differ.
/// </summary>
internal static class SmartDocsComposition
{
    /// <summary>
    /// Add the offline SmartDocs pipeline (embedder, store, retriever, reranker,
    /// graph retriever, chunk lookup, ingest sink) to <paramref name="services"/>.
    /// The corpus is embedded and indexed eagerly so the first tool call is fast.
    /// </summary>
    public static IServiceCollection AddSmartDocsOfflinePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var chunks = Corpus.Build();

        // Embedder + embedding service (query prefix is a pass-through offline).
        var generator = new BagOfWordsEmbeddingGenerator();
        var embeddingService = new EmbeddingService(
            generator, "bag-of-words-256", dimensions: 256, NullLogger<EmbeddingService>.Instance);

        // Index the corpus into the in-memory vector store.
        var store = new InMemoryVectorStore("ch18-mcp");
        store.EnsureCollectionExistsAsync().GetAwaiter().GetResult();
        foreach (var chunk in chunks)
        {
            var embedded = embeddingService.EmbedAsync(chunk).GetAwaiter().GetResult();
            store.UpsertAsync([embedded]).GetAwaiter().GetResult();
        }

        services.AddSingleton<IEmbeddingService>(embeddingService);
        services.AddSingleton<IVectorStore>(store);
        services.AddSingleton<IRetriever>(sp =>
            new DenseRetriever(sp.GetRequiredService<IEmbeddingService>(), sp.GetRequiredService<IVectorStore>()));
        services.AddSingleton<IReranker, PassThroughReranker>();

        // By-id lookup over the same chunks (get_chunk + ChunkResource).
        services.AddSingleton<IChunkLookup>(new InMemoryChunkLookup(chunks));

        // Stub LazyGraphRAG: offline chat client + a tiny deterministic graph.
        services.AddSingleton(_ =>
        {
            var chat = new OfflineChatClient();
            var entities = new[]
            {
                new GraphEntity("policy", "Document", "policy", new Dictionary<string, string>()),
                new GraphEntity("department", "Department", "department", new Dictionary<string, string>()),
                new GraphEntity("office", "Office", "office", new Dictionary<string, string>()),
            };
            var graphStore = new StubGraphStore(entities);
            var extractor = new EntityExtractor(chat);
            return new LazyGraphRagRetriever(extractor, graphStore, chat);
        });

        // Ingest write side (stub).
        services.AddSingleton<IIngestSink, InMemoryIngestSink>();

        return services;
    }
}
