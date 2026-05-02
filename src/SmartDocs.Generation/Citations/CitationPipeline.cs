using System.Text.RegularExpressions;
using SmartDocs.Core.Documents;

namespace SmartDocs.Generation.Citations;

/// <summary>One claim in the answer paired with its supporting source.</summary>
public sealed record Citation(
    string ClaimText,
    int SourceIndex,
    string ChunkId,
    string DocumentId,
    string Title,
    string Confidence);

/// <summary>The grounded answer plus its extracted citations.</summary>
public sealed record GroundedAnswer(
    string Answer,
    IReadOnlyList<Citation> Citations,
    double FaithfulnessScore);

/// <summary>
/// Extracts <c>[Source N]</c> markers from a generated answer and maps each
/// back to the corresponding chunk in the supplied source list. Pairs
/// nicely with <c>SmartDocs.Evaluation.GenerationEvaluator</c> for
/// the faithfulness score.
/// </summary>
public sealed partial class CitationPipeline
{
    [GeneratedRegex(@"\[Source (\d+)\]")]
    private static partial Regex CitationMarker();

    public static IReadOnlyList<Citation> ExtractCitations(
        string answer,
        IReadOnlyList<RetrievalResult> sources,
        string defaultConfidence = "SUPPORTED")
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(sources);

        var citations = new List<Citation>();
        var matches = CitationMarker().Matches(answer);
        foreach (Match m in matches)
        {
            if (!int.TryParse(m.Groups[1].Value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var idx))
            {
                continue;
            }
            if (idx < 1 || idx > sources.Count)
            {
                continue;
            }
            var src = sources[idx - 1];
            // Find the surrounding sentence as the claim text.
            var claim = ExtractSurroundingSentence(answer, m.Index);
            citations.Add(new Citation(
                ClaimText: claim,
                SourceIndex: idx,
                ChunkId: src.Chunk.ChunkId,
                DocumentId: src.Chunk.DocumentId,
                Title: src.Chunk.Metadata.Title,
                Confidence: defaultConfidence));
        }
        return citations;
    }

    private static string ExtractSurroundingSentence(string text, int markerIndex)
    {
        // Walk backwards/forwards to nearest sentence boundary.
        int start = markerIndex;
        while (start > 0 && text[start - 1] != '.' && text[start - 1] != '\n')
        {
            start--;
        }

        int end = markerIndex;
        while (end < text.Length && text[end] != '.' && text[end] != '\n')
        {
            end++;
        }

        return text[start..end].Trim();
    }
}
