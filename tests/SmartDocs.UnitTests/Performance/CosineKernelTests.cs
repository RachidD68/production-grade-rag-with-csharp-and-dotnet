using SmartDocs.Core.Numerics;

namespace SmartDocs.UnitTests.Performance;

public sealed class CosineKernelTests
{
    [Fact]
    public void Identical_vectors_have_cosine_one()
    {
        float[] v = [1f, 2f, 3f, 4f];
        Assert.Equal(1.0, CosineKernel.Cosine(v, v), precision: 6);
    }

    [Fact]
    public void Orthogonal_vectors_have_cosine_zero()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        Assert.Equal(0.0, CosineKernel.Cosine(a, b), precision: 6);
    }

    [Fact]
    public void Opposite_vectors_have_cosine_minus_one()
    {
        float[] a = [1f, 1f, 1f];
        float[] b = [-1f, -1f, -1f];
        Assert.Equal(-1.0, CosineKernel.Cosine(a, b), precision: 6);
    }

    [Fact]
    public void Known_vectors_match_hand_computed_value()
    {
        // a·b = 1*3 + 2*4 = 11; |a| = sqrt(5); |b| = 5; cos = 11 / (sqrt(5)*5)
        float[] a = [1f, 2f];
        float[] b = [3f, 4f];
        var expected = 11.0 / (Math.Sqrt(5) * 5);
        Assert.Equal(expected, CosineKernel.Cosine(a, b), precision: 9);
    }

    [Fact]
    public void Mismatched_lengths_return_zero()
    {
        Assert.Equal(0.0, CosineKernel.Cosine([1f, 2f, 3f], [1f, 2f]));
    }

    [Fact]
    public void Zero_vector_returns_zero()
    {
        Assert.Equal(0.0, CosineKernel.Cosine([0f, 0f, 0f], [1f, 2f, 3f]));
    }

    [Fact]
    public void Simd_path_agrees_with_scalar_reference_over_a_long_vector()
    {
        // A vector longer than any SIMD width exercises the vectorised loop AND
        // its scalar tail; compare to an independent scalar reference.
        var rng = new Random(42);
        const int n = 1_000; // not a multiple of 8/16, so the tail runs too
        var a = new float[n];
        var b = new float[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = (float)(rng.NextDouble() * 2 - 1);
            b[i] = (float)(rng.NextDouble() * 2 - 1);
        }

        var kernel = CosineKernel.Cosine(a, b);
        var reference = ScalarCosine(a, b);

        Assert.Equal(reference, kernel, precision: 5);
    }

    // An independent, deliberately naive scalar implementation used as the oracle.
    private static double ScalarCosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double dot = 0, ma = 0, mb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            ma += a[i] * a[i];
            mb += b[i] * b[i];
        }
        return ma == 0 || mb == 0 ? 0 : dot / (Math.Sqrt(ma) * Math.Sqrt(mb));
    }
}
