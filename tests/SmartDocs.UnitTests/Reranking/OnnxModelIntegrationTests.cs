using SmartDocs.Reranking;
using SmartDocs.Reranking.Onnx;

namespace SmartDocs.UnitTests.Reranking;

/// <summary>
/// Integration coverage for the real ONNX cross-encoder. Skipped by default:
/// it needs the bge-reranker-v2-m3 model files, which CI does not ship. Drop
/// the exported <c>model.onnx</c> and <c>sentencepiece.bpe.model</c> next to the
/// test binary (or edit the paths) and remove the Skip to exercise the genuine
/// inference path end to end.
/// </summary>
public sealed class OnnxModelIntegrationTests
{
    [Fact(Skip = "requires bge-reranker-v2-m3 model files")]
    public void Scores_documents_with_real_model()
    {
        const string onnxModelPath = "bge-reranker-v2-m3/model.onnx";
        const string sentencePieceModelPath = "bge-reranker-v2-m3/sentencepiece.bpe.model";

        using var model = BgeOnnxCrossEncoderModel.Create(onnxModelPath, sentencePieceModelPath);
        var reranker = new OnnxCrossEncoderReranker(model);

        var scores = model.Score(
            "How many vacation days do I get?",
            ["Employees accrue twenty paid vacation days per year.", "The deployment pipeline is blue-green."]);

        Assert.Equal(2, scores.Count);
        Assert.True(scores[0] > scores[1]);
        Assert.NotNull(reranker);
    }
}
