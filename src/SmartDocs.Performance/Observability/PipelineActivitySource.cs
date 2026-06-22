using System.Diagnostics;

namespace SmartDocs.Performance.Observability;

/// <summary>
/// The single <see cref="ActivitySource"/> the RAG pipeline emits spans from
/// (Ch 21). Each stage — retrieve, augment, generate — opens a child span, so a
/// trace in the Aspire dashboard (or any OTLP backend) shows exactly where a
/// slow request spent its time. The source name <c>SmartDocs.Pipeline</c> is the
/// handle telemetry wiring subscribes to via
/// <see cref="SmartDocsTelemetry.AddSmartDocsTelemetry"/>.
/// <para>
/// A span is only created when something is listening (an
/// <see cref="ActivityListener"/> or an OpenTelemetry tracer provider has
/// registered the source); otherwise <see cref="StartStage"/> returns
/// <see langword="null"/> at near-zero cost, so instrumenting the hot path is
/// free when tracing is off.
/// </para>
/// </summary>
public static class PipelineActivitySource
{
    /// <summary>The activity-source name external listeners subscribe to.</summary>
    public const string SourceName = "SmartDocs.Pipeline";

    /// <summary>The shared source. Long-lived; never disposed for the process lifetime.</summary>
    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Start the <c>retrieve</c> stage span.</summary>
    public static Activity? StartRetrieve() => StartStage("rag.retrieve");

    /// <summary>Start the <c>augment</c> stage span.</summary>
    public static Activity? StartAugment() => StartStage("rag.augment");

    /// <summary>Start the <c>generate</c> stage span.</summary>
    public static Activity? StartGenerate() => StartStage("rag.generate");

    /// <summary>
    /// Start an internal span named <paramref name="stageName"/>. Returns
    /// <see langword="null"/> when no listener is attached. Dispose the returned
    /// activity to close the span (a <c>using</c> at the call site).
    /// </summary>
    /// <param name="stageName">The span name, e.g. <c>rag.retrieve</c>.</param>
    public static Activity? StartStage(string stageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        return Source.StartActivity(stageName, ActivityKind.Internal);
    }
}
