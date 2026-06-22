using System.Diagnostics;
using SmartDocs.Performance.Observability;

namespace SmartDocs.UnitTests.Performance;

public sealed class PipelineActivitySourceTests
{
    [Fact]
    public void StartStage_emits_a_span_when_a_listener_is_attached()
    {
        var started = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == PipelineActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => started.Add(a.DisplayName),
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = PipelineActivitySource.StartRetrieve())
        {
            Assert.NotNull(activity);
            Assert.Equal("rag.retrieve", activity!.DisplayName);
        }

        Assert.Contains("rag.retrieve", started);
    }

    [Fact]
    public void StartStage_returns_null_when_no_listener_is_attached()
    {
        // No listener registered for the source ⇒ no span is created, so
        // instrumenting the hot path is free when tracing is off.
        using var activity = PipelineActivitySource.StartStage("rag.generate.unlistened");
        Assert.Null(activity);
    }
}
