using SmartDocs.Core.Numerics;

namespace SmartDocs.Security;

/// <summary>
/// Embedding-space anomaly detector. Computes the centroid of a "normal"
/// reference set and flags any vector whose cosine similarity to that
/// centroid falls below <see cref="MinSimilarityThreshold"/>. Used to
/// quarantine documents whose embeddings look adversarial — outliers
/// designed to land on top of unrelated queries.
/// </summary>
public sealed class EmbeddingAnomalyDetector
{
    public double MinSimilarityThreshold { get; }
    private float[]? _centroid;

    public EmbeddingAnomalyDetector(double minSimilarityThreshold = 0.3)
    {
        MinSimilarityThreshold = minSimilarityThreshold;
    }

    public void Fit(IReadOnlyList<ReadOnlyMemory<float>> referenceVectors)
    {
        ArgumentNullException.ThrowIfNull(referenceVectors);
        if (referenceVectors.Count == 0)
        {
            throw new ArgumentException("Need at least one reference vector.", nameof(referenceVectors));
        }
        int dim = referenceVectors[0].Length;
        var centroid = new float[dim];
        foreach (var v in referenceVectors)
        {
            if (v.Length != dim)
            {
                throw new ArgumentException("Reference vectors must share dimensionality.");
            }
            var span = v.Span;
            for (int i = 0; i < dim; i++)
            {
                centroid[i] += span[i];
            }
        }
        for (int i = 0; i < dim; i++)
        {
            centroid[i] /= referenceVectors.Count;
        }
        _centroid = centroid;
    }

    public bool IsAnomalous(ReadOnlyMemory<float> vector, out double similarity)
    {
        if (_centroid is null)
        {
            throw new InvalidOperationException("Fit(...) must be called first.");
        }
        similarity = CosineKernel.Cosine(vector.Span, _centroid);
        return similarity < MinSimilarityThreshold;
    }
}
