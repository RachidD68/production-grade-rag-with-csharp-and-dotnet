using System.Text.Json.Serialization;

namespace RagInDotNet.Tools.EvalRunner;

/// <summary>
/// A serialisable snapshot of one eval run's per-query outcomes — enough for the
/// next run to compute a <em>paired</em> comparison (a per-item delta, not just
/// a difference of aggregates). Persisted as JSON by <c>--write-baseline</c> and
/// read back by <c>--baseline</c>.
/// </summary>
/// <param name="PerQueryFaithfulness">Faithfulness score for each gold query, in query order.</param>
/// <param name="PerQueryHit">Whether top-K retrieval contained a gold document, in query order.</param>
internal sealed record EvalBaseline(
    IReadOnlyList<double> PerQueryFaithfulness,
    IReadOnlyList<bool> PerQueryHit);

/// <summary>Source-generated JSON context for <see cref="EvalBaseline"/> (trim/AOT friendly).</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(EvalBaseline))]
internal sealed partial class EvalJsonContext : JsonSerializerContext;
