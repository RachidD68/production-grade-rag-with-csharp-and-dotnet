using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace RagInDotNet.Samples.Ch21_AotMcpServer;

/// <summary>
/// A minimal, fully self-contained MCP tool: a substring search over a tiny
/// in-memory corpus. No retrieval pipeline, no provider SDKs — exactly the
/// surface that can be Native-AOT published cleanly. Tool parameters and the
/// returned <see cref="DocHit"/> are described to the source-generated JSON
/// context (<see cref="AotJsonContext"/>), so no reflection-based serialization
/// is needed at runtime.
/// </summary>
[McpServerToolType]
public sealed class DocsSearchTool
{
    private static readonly (string Id, string Text)[] Corpus =
    [
        ("hr-001", "Employees receive 20 paid vacation days per fiscal year."),
        ("hr-002", "Remote work is allowed up to 3 days per week with approval."),
        ("eng-001", "Production deploys run through the blue-green pipeline."),
        ("fin-001", "Expense reports are reimbursed within 30 days of submission."),
    ];

    /// <summary>Return up to <paramref name="k"/> corpus entries whose text contains the query.</summary>
    [McpServerTool(Name = "search", ReadOnly = true), Description(
        "Search the in-memory docs corpus and return up to K matching entries.")]
    public static IReadOnlyList<DocHit> Search(
        [Description("The search query (case-insensitive substring match).")] string query,
        [Description("Maximum number of hits to return (default 3).")] int k = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var capped = Math.Clamp(k, 1, Corpus.Length);
        return [.. Corpus
            .Where(d => d.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(capped)
            .Select(d => new DocHit(d.Id, d.Text))];
    }
}

/// <summary>A single search hit: the document id and its text.</summary>
/// <param name="Id">The document identifier.</param>
/// <param name="Text">The matching document text.</param>
public sealed record DocHit(string Id, string Text);

/// <summary>
/// System.Text.Json source-generation context for the tool's payloads. Using a
/// generated context (rather than reflection-based serialization) is what keeps
/// the JSON path AOT-safe — the serializer code for <see cref="DocHit"/> is
/// emitted at compile time.
/// </summary>
[JsonSerializable(typeof(DocHit))]
[JsonSerializable(typeof(IReadOnlyList<DocHit>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
public sealed partial class AotJsonContext : JsonSerializerContext;
