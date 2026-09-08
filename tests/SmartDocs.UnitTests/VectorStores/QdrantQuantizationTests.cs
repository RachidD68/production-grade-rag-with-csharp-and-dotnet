using Qdrant.Client.Grpc;
using SmartDocs.Retrieval.VectorStores;

namespace SmartDocs.UnitTests.VectorStores;

/// <summary>
/// Unit tests for the opt-in scalar-quantization flag on
/// <see cref="QdrantVectorStore"/>. These assert on the collection-creation
/// params (<see cref="QdrantVectorStore.BuildVectorParams"/>) directly, so no
/// live Qdrant is required.
/// </summary>
public sealed class QdrantQuantizationTests
{
    [Fact]
    public void Quantization_absent_by_default()
    {
        var vectorParams = QdrantVectorStore.BuildVectorParams(768, Distance.Cosine, useScalarQuantization: false);

        Assert.Equal(768ul, vectorParams.Size);
        Assert.Equal(Distance.Cosine, vectorParams.Distance);
        // Default path must be byte-identical to the original bare VectorParams.
        Assert.Null(vectorParams.QuantizationConfig);
    }

    [Fact]
    public void Quantization_set_to_int8_scalar_when_enabled()
    {
        var vectorParams = QdrantVectorStore.BuildVectorParams(768, Distance.Cosine, useScalarQuantization: true);

        Assert.NotNull(vectorParams.QuantizationConfig);
        Assert.Equal(
            QuantizationConfig.QuantizationOneofCase.Scalar,
            vectorParams.QuantizationConfig.QuantizationCase);
        Assert.Equal(QuantizationType.Int8, vectorParams.QuantizationConfig.Scalar.Type);
        // Qdrant.Client 1.19: the storage tier replaces the retired AlwaysRam flag.
        Assert.True(vectorParams.QuantizationConfig.Scalar.HasMemory);
        Assert.Equal(Memory.Pinned, vectorParams.QuantizationConfig.Scalar.Memory);
    }
}
