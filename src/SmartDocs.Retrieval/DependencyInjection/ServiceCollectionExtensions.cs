using Azure;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SmartDocs.Core.Abstractions;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.Retrieval.DependencyInjection;

/// <summary>
/// DI registration extensions for the SmartDocs <see cref="IVectorStore"/>
/// adapters. These give the tidy one-liner experience the chapter promises
/// while keeping the hand-rolled domain port as the abstraction. The names are
/// SmartDocs-scoped on purpose — they register <see cref="IVectorStore"/>
/// (our port), not <c>Microsoft.Extensions.VectorData</c> types.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory <see cref="IVectorStore"/> (brute-force cosine).
    /// The shared test/dev/Hello-World backend; no external dependency.
    /// </summary>
    public static IServiceCollection AddSmartDocsInMemoryVectorStore(
        this IServiceCollection services,
        string collectionName = "in-memory")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        services.AddSingleton<IVectorStore>(_ => new InMemoryVectorStore(collectionName));
        return services;
    }

    /// <summary>
    /// Registers the Qdrant-backed <see cref="IVectorStore"/> over a gRPC
    /// <see cref="QdrantClient"/> built from <paramref name="host"/> /
    /// <paramref name="port"/> (gRPC default 6334).
    /// </summary>
    /// <param name="services">The DI container being built.</param>
    /// <param name="collectionName">The Qdrant collection backing the store.</param>
    /// <param name="vectorSize">Embedding dimensionality.</param>
    /// <param name="host">Qdrant host (default <c>localhost</c>).</param>
    /// <param name="port">Qdrant gRPC port (default 6334).</param>
    /// <param name="distance">Similarity metric (default cosine).</param>
    /// <param name="useScalarQuantization">
    /// Opt in to int8 scalar quantization at collection creation (off by
    /// default — the baseline collection layout is unchanged).
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddSmartDocsQdrantVectorStore(
        this IServiceCollection services,
        string collectionName,
        int vectorSize,
        string host = "localhost",
        int port = 6334,
        Distance distance = Distance.Cosine,
        bool useScalarQuantization = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vectorSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        services.AddSingleton<IVectorStore>(_ =>
            new QdrantVectorStore(
                new QdrantClient(host, port),
                collectionName,
                vectorSize,
                distance,
                useScalarQuantization));
        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search-backed <see cref="IVectorStore"/> using an
    /// admin <see cref="AzureKeyCredential"/>.
    /// </summary>
    public static IServiceCollection AddSmartDocsAzureAiSearchVectorStore(
        this IServiceCollection services,
        Uri endpoint,
        string indexName,
        int vectorSize,
        AzureKeyCredential credential)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vectorSize);
        ArgumentNullException.ThrowIfNull(credential);

        services.AddSingleton<IVectorStore>(_ =>
            new AzureAiSearchVectorStore(endpoint, indexName, vectorSize, credential));
        return services;
    }

    /// <summary>
    /// Registers the Azure AI Search-backed <see cref="IVectorStore"/> using an
    /// Entra ID <see cref="TokenCredential"/> (e.g. managed identity).
    /// </summary>
    public static IServiceCollection AddSmartDocsAzureAiSearchVectorStore(
        this IServiceCollection services,
        Uri endpoint,
        string indexName,
        int vectorSize,
        TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vectorSize);
        ArgumentNullException.ThrowIfNull(credential);

        services.AddSingleton<IVectorStore>(_ =>
            new AzureAiSearchVectorStore(endpoint, indexName, vectorSize, credential));
        return services;
    }
}
