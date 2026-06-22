using MathNet.Numerics.LinearAlgebra;

namespace SmartDocs.Operations;

/// <summary>
/// Drift-Adapter (Ch 22) — learns the optimal rigid rotation that maps an old
/// embedding model's space onto a new model's space, then applies it at query
/// time so an existing index can keep its old vectors instead of being fully
/// re-embedded.
///
/// <para>
/// This is a real <strong>Orthogonal Procrustes</strong> solve (no longer the
/// Phase-5 identity placeholder). Given <em>N</em> paired vectors — the same
/// texts embedded by the old model (rows of <c>A</c>) and the new model (rows of
/// <c>B</c>) — it finds the rotation <c>R</c> minimising
/// <c>‖A·R − B‖_F</c>:
/// </para>
/// <list type="number">
///   <item><description>Form the cross-covariance <c>C = Aᵀ·B</c>.</description></item>
///   <item><description>Take its singular value decomposition <c>C = U·Σ·Vᵀ</c>.</description></item>
///   <item><description>The optimal rotation is <c>R = U·Vᵀ</c> (the closed-form Procrustes solution).</description></item>
/// </list>
/// <para>
/// <see cref="Apply"/> multiplies a query vector by <c>R</c> and re-normalises the
/// result to unit length, because cosine similarity assumes unit vectors (Ch 22
/// §6). The map is a same-dimension rotation: it cannot up-project a smaller old
/// space into a larger new one, so a dimension change (e.g. 1536 → 3072) must be
/// handled by full re-embedding rather than by this adapter — <see cref="Train"/>
/// guards against mismatched dimensions explicitly.
/// </para>
/// </summary>
public sealed class DriftAdapter
{
    // Stored as a MathNet double matrix so Apply reuses the same linear-algebra
    // kernel the solve used; float<->double conversion happens at the boundary.
    private Matrix<double>? _transform;

    /// <summary>
    /// Train the adapter from <em>N</em> pairs of (old-model embedding, new-model
    /// embedding). Both arrays must be the same length and every vector must share
    /// one common dimensionality; the solve is a same-dimension rotation.
    /// </summary>
    /// <param name="oldVectors">Old-model embeddings (rows of <c>A</c>).</param>
    /// <param name="newVectors">New-model embeddings of the same texts (rows of <c>B</c>).</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the arrays are empty, of unequal length, or contain vectors of
    /// differing dimensionality (old≠new dimension is an up-projection a square
    /// rotation cannot represent).
    /// </exception>
    public void Train(ReadOnlyMemory<float>[] oldVectors, ReadOnlyMemory<float>[] newVectors)
    {
        ArgumentNullException.ThrowIfNull(oldVectors);
        ArgumentNullException.ThrowIfNull(newVectors);
        if (oldVectors.Length == 0 || oldVectors.Length != newVectors.Length)
        {
            throw new ArgumentException("Need a non-zero, equal number of old and new training vectors.");
        }

        int dim = oldVectors[0].Length;
        if (dim == 0)
        {
            throw new ArgumentException("Training vectors must be non-empty.", nameof(oldVectors));
        }

        // Every paired vector must share the one rotation dimension. A differing
        // old/new dimension is an up-/down-projection the orthogonal map cannot
        // represent — re-embed instead (Ch 22 §6).
        var a = Matrix<double>.Build.Dense(oldVectors.Length, dim);
        var b = Matrix<double>.Build.Dense(newVectors.Length, dim);
        for (int row = 0; row < oldVectors.Length; row++)
        {
            var oldSpan = oldVectors[row].Span;
            var newSpan = newVectors[row].Span;
            if (oldSpan.Length != dim || newSpan.Length != dim)
            {
                throw new ArgumentException(
                    $"All training vectors must share dimension {dim}; the Procrustes rotation " +
                    "cannot change dimensionality (a dimension change requires full re-embedding).");
            }

            for (int col = 0; col < dim; col++)
            {
                a[row, col] = oldSpan[col];
                b[row, col] = newSpan[col];
            }
        }

        // C = Aᵀ·B  (cross-covariance), then SVD(C) = U·Σ·Vᵀ, R = U·Vᵀ.
        var c = a.TransposeThisAndMultiply(b);
        var svd = c.Svd(computeVectors: true);
        // R = U·Vᵀ. MathNet exposes Vᵀ directly as svd.VT.
        _transform = svd.U.Multiply(svd.VT);
    }

    /// <summary>
    /// Apply the trained rotation to a new query vector and re-normalise the result
    /// to unit length (cosine similarity assumes unit vectors).
    /// </summary>
    /// <param name="queryVector">The query embedding to rotate into the old space.</param>
    /// <returns>The rotated, unit-normalised vector.</returns>
    /// <exception cref="InvalidOperationException">Thrown if <see cref="Train"/> has not run.</exception>
    /// <exception cref="ArgumentException">Thrown if the query dimension differs from the trained dimension.</exception>
    public float[] Apply(ReadOnlyMemory<float> queryVector)
    {
        if (_transform is null)
        {
            throw new InvalidOperationException("Train(...) must be called before Apply.");
        }

        var src = queryVector.Span;
        int dim = src.Length;
        if (_transform.RowCount != dim)
        {
            throw new ArgumentException($"Trained transform is {_transform.RowCount}-dim; got {dim}-dim query.");
        }

        // result = qᵀ·R  (R is dim×dim, so the rotated vector is R-multiplied on
        // the same side the rows of A were). We compute (Rᵀ·q) which equals the
        // row-vector product qᵀ·R transposed back to a column.
        var q = Vector<double>.Build.Dense(dim);
        for (int i = 0; i < dim; i++)
        {
            q[i] = src[i];
        }

        var rotated = _transform.TransposeThisAndMultiply(q);

        double norm = rotated.L2Norm();
        var result = new float[dim];
        if (norm > 1e-12)
        {
            for (int i = 0; i < dim; i++)
            {
                result[i] = (float)(rotated[i] / norm);
            }
        }
        else
        {
            for (int i = 0; i < dim; i++)
            {
                result[i] = (float)rotated[i];
            }
        }

        return result;
    }
}
