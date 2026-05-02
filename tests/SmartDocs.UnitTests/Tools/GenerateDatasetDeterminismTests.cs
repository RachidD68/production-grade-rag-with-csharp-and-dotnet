using System.Security.Cryptography;
using System.Text;
using RagInDotNet.Tools.GenerateDataset;

namespace SmartDocs.UnitTests.Tools;

/// <summary>
/// Phase 0 smoke test: the generate-dataset tool must produce a byte-identical
/// dataset across two runs with the same seed. The dataset is the foundation
/// of every chapter eval — it cannot drift.
/// </summary>
public sealed class GenerateDatasetDeterminismTests
{
    [Fact]
    public void Small_dataset_is_deterministic_across_runs()
    {
        // Arrange — two scratch directories.
        var rootA = Path.Combine(Path.GetTempPath(), "rag-tests", Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "rag-tests", Guid.NewGuid().ToString("N"));

        try
        {
            // Act — run the generator twice with the same seed.
            DatasetGenerator.Run(seed: 42, output: new DirectoryInfo(rootA), DatasetSize.Small);
            DatasetGenerator.Run(seed: 42, output: new DirectoryInfo(rootB), DatasetSize.Small);

            // Assert — file count.
            var filesA = Directory.GetFiles(rootA, "*.md", SearchOption.AllDirectories);
            var filesB = Directory.GetFiles(rootB, "*.md", SearchOption.AllDirectories);
            Assert.Equal(300, filesA.Length);
            Assert.Equal(300, filesB.Length);

            // Assert — every silo populated with the documented count.
            AssertSiloCount(rootA, "hr-policies", 40);
            AssertSiloCount(rootA, "technical-docs", 120);
            AssertSiloCount(rootA, "financial-reports", 30);
            AssertSiloCount(rootA, "legal-contracts", 50);
            AssertSiloCount(rootA, "product-catalog", 30);
            AssertSiloCount(rootA, "release-notes-tickets", 30);

            // Assert — manifests are byte-identical.
            var manifestA = File.ReadAllBytes(Path.Combine(rootA, "manifest.sha256"));
            var manifestB = File.ReadAllBytes(Path.Combine(rootB, "manifest.sha256"));
            Assert.Equal(manifestA, manifestB);

            // Assert — combined-content SHA matches the manifest.
            // (The manifest is the per-file SHA list; we double-check by recomputing.)
            var rebuilt = BuildManifest(rootA);
            var manifestText = Encoding.UTF8.GetString(manifestA);
            foreach (var line in rebuilt)
            {
                Assert.Contains(line, manifestText, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (Directory.Exists(rootA))
            {
                Directory.Delete(rootA, recursive: true);
            }
            if (Directory.Exists(rootB))
            {
                Directory.Delete(rootB, recursive: true);
            }
        }
    }

    private static void AssertSiloCount(string root, string silo, int expected)
    {
        var dir = Path.Combine(root, silo);
        Assert.True(Directory.Exists(dir), $"silo directory missing: {silo}");
        var count = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly).Length;
        Assert.Equal(expected, count);
    }

    private static List<string> BuildManifest(string root)
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            lines.Add($"{hash}  {rel}");
        }
        return lines;
    }
}
