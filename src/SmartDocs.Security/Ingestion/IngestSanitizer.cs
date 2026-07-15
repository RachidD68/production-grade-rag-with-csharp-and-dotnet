using SmartDocs.Core.Documents;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security.Ingestion;

/// <summary>
/// Gate on the ingest path. Every chunk is run through an
/// <see cref="IInjectionDetector"/> before it is allowed into the index:
/// <list type="bullet">
///   <item>If an injection is detected at or above <see cref="QuarantineThreshold"/>,
///   the chunk is quarantined — a <see cref="SecurityIncident"/> is raised and
///   <see cref="IngestRejected"/> is thrown.</item>
///   <item>Otherwise the chunk is stamped with a provenance signature
///   (<see cref="DocumentChunk.Provenance"/>) and returned for indexing.</item>
/// </list>
/// This is the defense against indirect / planted-document prompt injection: a
/// poisoned document is stopped at the door rather than at query time.
/// </summary>
public sealed class IngestSanitizer
{
    private readonly IInjectionDetector _detector;
    private readonly IProvenanceSigner _signer;
    private readonly ISecurityAlertSink _alert;

    /// <summary>
    /// Score at or above which a detected injection causes quarantine. Defaults
    /// to 0.8 so a confident heuristic hit (score 1.0) always quarantines.
    /// </summary>
    public double QuarantineThreshold { get; }

    public IngestSanitizer(
        IInjectionDetector detector,
        IProvenanceSigner signer,
        ISecurityAlertSink alert,
        double quarantineThreshold = 0.8)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentOutOfRangeException.ThrowIfNegative(quarantineThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(quarantineThreshold, 1.0);

        _detector = detector;
        _signer = signer;
        _alert = alert;
        QuarantineThreshold = quarantineThreshold;
    }

    /// <summary>
    /// Analyzes <paramref name="chunk"/>, quarantining it on a confident
    /// injection hit, otherwise returning a signed copy.
    /// </summary>
    public async Task<DocumentChunk> SanitizeAsync(
        DocumentChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var analysis = await _detector
            .AnalyzeAsync(chunk.Text, documents: null, cancellationToken)
            .ConfigureAwait(false);

        if (analysis.Detected && analysis.Score >= QuarantineThreshold)
        {
            var incident = new SecurityIncident(
                Kind: "ingest-injection-quarantined",
                Detail: $"Chunk '{chunk.ChunkId}' quarantined at ingest (score {analysis.Score:0.00}): "
                      + string.Join(", ", analysis.Findings),
                DetectedAtUtc: DateTimeOffset.UtcNow);
            await _alert.RaiseAsync(incident, cancellationToken).ConfigureAwait(false);
            throw new IngestRejected(chunk.ChunkId, analysis);
        }

        return chunk with { Provenance = _signer.Sign(chunk) };
    }
}
