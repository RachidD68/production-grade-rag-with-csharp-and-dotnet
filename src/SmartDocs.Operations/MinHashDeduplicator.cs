using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Operations;

/// <summary>
/// MinHash + Locality-Sensitive Hashing-style deduplicator over
/// <see cref="DocumentChunk"/>s. Computes a 64-permutation MinHash signature
/// over word-1-shingles, then groups documents whose Jaccard similarity
/// estimate exceeds <see cref="Threshold"/>. The dedup output is one
/// representative per group.
/// </summary>
public sealed partial class MinHashDeduplicator
{
    public int Permutations { get; }
    public double Threshold { get; }

    private readonly uint[] _seedsA;
    private readonly uint[] _seedsB;

    public MinHashDeduplicator(int permutations = 64, double threshold = 0.85, int seed = 42)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permutations);
        Permutations = permutations;
        Threshold = threshold;
        var rng = new Random(seed);
        _seedsA = new uint[permutations];
        _seedsB = new uint[permutations];
        for (int i = 0; i < permutations; i++)
        {
            _seedsA[i] = (uint)(rng.Next() | 1); // odd
            _seedsB[i] = (uint)rng.Next();
        }
    }

    [GeneratedRegex(@"[\p{L}\d_]+", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    public IReadOnlyList<DocumentChunk> Deduplicate(IEnumerable<DocumentChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        var list = chunks.ToList();
        var sigs = list.Select(c => Signature(c.Text)).ToList();

        var keep = new List<DocumentChunk>();
        var keepSigs = new List<uint[]>();
        foreach (var (chunk, sig) in list.Zip(sigs))
        {
            bool dup = false;
            for (int i = 0; i < keepSigs.Count; i++)
            {
                if (JaccardEstimate(sig, keepSigs[i]) >= Threshold)
                {
                    dup = true;
                    break;
                }
            }
            if (!dup)
            {
                keep.Add(chunk);
                keepSigs.Add(sig);
            }
        }
        return keep;
    }

    private uint[] Signature(string text)
    {
        var sig = new uint[Permutations];
        for (int i = 0; i < Permutations; i++)
        {
            sig[i] = uint.MaxValue;
        }

        foreach (var token in Tokenize(text))
        {
            var h = StableHash(token);
            for (int i = 0; i < Permutations; i++)
            {
                var hi = unchecked(_seedsA[i] * h + _seedsB[i]);
                if (hi < sig[i])
                {
                    sig[i] = hi;
                }
            }
        }
        return sig;
    }

    private static IEnumerable<string> Tokenize(string text) =>
        Token().Matches(text).Select(m => m.Value.ToLowerInvariant());

    private static uint StableHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return BitConverter.ToUInt32(bytes, 0);
    }

    public static double JaccardEstimate(uint[] a, uint[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length)
        {
            return 0;
        }

        int agree = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i])
            {
                agree++;
            }
        }
        return (double)agree / a.Length;
    }
}
