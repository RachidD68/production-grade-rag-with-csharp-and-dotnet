using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Ingestion.Chunking;

/// <summary>
/// Splits text by walking a hierarchy of separators ("\n\n", "\n", ". ", " ")
/// — markdown-aware: section boundaries first, paragraphs second, sentences
/// third, words last. Re-merges adjacent splits until each piece fits the
/// configured <see cref="MaxChunkSize"/>.
/// </summary>
public sealed class RecursiveCharacterChunker : IChunker
{
    public int MaxChunkSize { get; }
    public IReadOnlyList<string> Separators { get; }

    public RecursiveCharacterChunker(int maxChunkSize = 800, IReadOnlyList<string>? separators = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunkSize);
        MaxChunkSize = maxChunkSize;
        Separators = separators ?? ["\n## ", "\n# ", "\n\n", "\n", ". ", " "];
    }

    public string Strategy => "recursive";

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await Task.Yield();

        var text = document.Content;
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        int index = 0;
        foreach (var (start, end) in Split(text, 0, text.Length, separatorIndex: 0))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ChunkBuilder.Build(document, index++, start, end, text[start..end].Trim());
        }
    }

    private IEnumerable<(int Start, int End)> Split(string text, int start, int end, int separatorIndex)
    {
        var len = end - start;
        if (len <= MaxChunkSize)
        {
            if (len > 0)
            {
                yield return (start, end);
            }

            yield break;
        }
        if (separatorIndex >= Separators.Count)
        {
            // Hard-cut: walk forward in MaxChunkSize-sized windows.
            int cursor = start;
            while (cursor < end)
            {
                int windowEnd = Math.Min(cursor + MaxChunkSize, end);
                yield return (cursor, windowEnd);
                cursor = windowEnd;
            }
            yield break;
        }

        var sep = Separators[separatorIndex];
        int pieceStart = start;
        int searchFrom = start;
        while (true)
        {
            int idx = text.IndexOf(sep, searchFrom, end - searchFrom, StringComparison.Ordinal);
            if (idx < 0)
            {
                break;
            }

            int pieceEnd = idx + sep.Length;
            foreach (var split in Split(text, pieceStart, pieceEnd, separatorIndex + 1))
            {
                yield return split;
            }
            pieceStart = pieceEnd;
            searchFrom = pieceEnd;
        }
        if (pieceStart < end)
        {
            foreach (var split in Split(text, pieceStart, end, separatorIndex + 1))
            {
                yield return split;
            }
        }
    }
}
