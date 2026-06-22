using System.Diagnostics.CodeAnalysis;

namespace SmartDocs.Security.Abstractions;

/// <summary>
/// Thrown by the ingest pipeline when a chunk is quarantined because an
/// <see cref="IInjectionDetector"/> judged it to be a planted (indirect)
/// prompt-injection payload at or above the quarantine threshold. The offending
/// chunk id and the triggering <see cref="InjectionAnalysis"/> are carried for
/// audit and triage.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1710:Identifiers should have correct suffix",
    Justification = "The chapter names this rejection signal 'IngestRejected'; the explicit type carries the chunk id + analysis and is matched by name in the ingest pipeline.")]
public sealed class IngestRejected : Exception
{
    /// <summary>Id of the chunk that was rejected.</summary>
    public string ChunkId { get; }

    /// <summary>The analysis that caused the rejection.</summary>
    public InjectionAnalysis Analysis { get; }

    public IngestRejected(string chunkId, InjectionAnalysis analysis)
        : base($"Ingest rejected chunk '{chunkId}': injection detected (score {analysis?.Score:0.00}).")
    {
        ArgumentException.ThrowIfNullOrEmpty(chunkId);
        ArgumentNullException.ThrowIfNull(analysis);
        ChunkId = chunkId;
        Analysis = analysis;
    }
}
