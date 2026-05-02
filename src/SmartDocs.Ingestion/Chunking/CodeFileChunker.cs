using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Chunking;

/// <summary>
/// Splits C# source files into chunks aligned to class and method
/// boundaries using Roslyn. Documents whose <c>SourcePath</c> doesn't end
/// in <c>.cs</c> fall back to whole-document chunks.
/// </summary>
public sealed class CodeFileChunker : IChunker
{
    public string Strategy => "code-cs";

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await Task.Yield();

        var text = document.Content;
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        if (!document.SourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            yield return ChunkBuilder.Build(document, 0, 0, text.Length, text);
            yield break;
        }

        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

        var members = root.DescendantNodes()
            .Where(n => n is BaseTypeDeclarationSyntax or BaseMethodDeclarationSyntax)
            .Select(n => n.FullSpan)
            .OrderBy(s => s.Start)
            .ToList();

        if (members.Count == 0)
        {
            yield return ChunkBuilder.Build(document, 0, 0, text.Length, text);
            yield break;
        }

        int index = 0;
        foreach (var span in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = Math.Min(span.Start, text.Length);
            int end = Math.Min(span.End, text.Length);
            if (end <= start)
            {
                continue;
            }

            yield return ChunkBuilder.Build(document, index++, start, end, text[start..end]);
        }
    }
}
