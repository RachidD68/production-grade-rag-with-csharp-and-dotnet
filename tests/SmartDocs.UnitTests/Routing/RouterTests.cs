using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SmartDocs.Ingestion.Embeddings;
using SmartDocs.Routing;

namespace SmartDocs.UnitTests.Routing;

public sealed partial class RouterTests
{
    // A deterministic, offline IEmbeddingService: a FNV-1a bag-of-words generator
    // (same scheme as the Ch07/Ch08 samples — no string.GetHashCode) wrapped in the
    // real EmbeddingService. The same text always yields the same vector, so the
    // embedding router routes reproducibly.
    private static EmbeddingService BagOfWordsService()
    {
        var generator = new StubEmbeddingGenerator(BagOfWordsVector);
        return new EmbeddingService(generator, "bag-of-words-256", 256, NullLogger<EmbeddingService>.Instance);
    }

    private static float[] BagOfWordsVector(string text)
    {
        const int dimensions = 256;
        var vec = new float[dimensions];
        foreach (Match m in WordRegex().Matches(text.ToLowerInvariant()))
        {
            vec[(int)(StableHash(m.Value) % dimensions)] += 1f;
        }
        var mag = MathF.Sqrt(vec.Sum(v => v * v));
        if (mag > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                vec[i] /= mag;
            }
        }
        return vec;
    }

    // FNV-1a (32-bit) — stable across processes, unlike string.GetHashCode.
    private static uint StableHash(string s)
    {
        uint hash = 2166136261;
        foreach (var ch in s)
        {
            hash ^= ch;
            hash *= 16777619;
        }
        return hash;
    }

    [GeneratedRegex(@"[a-z0-9][a-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [Fact]
    public async Task RuleBasedRouter_routes_HR_keywords_to_hr_silo()
    {
        var router = new RuleBasedRouter();
        var decision = await router.RouteAsync("How many vacation days do I get?");

        Assert.Contains("hr-policies", decision.Silos);
        Assert.True(decision.Confidence > 0);
    }

    [Fact]
    public async Task RuleBasedRouter_returns_zero_confidence_for_unknown_query()
    {
        var router = new RuleBasedRouter();
        var decision = await router.RouteAsync("Tell me about quantum mechanics");

        Assert.Empty(decision.Silos);
        Assert.Equal(0, decision.Confidence);
    }

    [Fact]
    public async Task LlmClassifierRouter_parses_LLM_classification_json()
    {
        var stub = new StubChatClient(_ =>
            "{ \"silos\": [\"financial-reports\"], \"confidence\": 0.9, \"reasoning\": \"asks about Q3 revenue\" }");
        var router = new LlmClassifierRouter(stub);

        var decision = await router.RouteAsync("What was Q3 revenue?");

        Assert.Single(decision.Silos);
        Assert.Equal("financial-reports", decision.Silos[0]);
        Assert.Equal(0.9, decision.Confidence);
        Assert.Equal("llm-classifier", decision.Strategy);
    }

    [Fact]
    public async Task LlmClassifierRouter_drops_unknown_silos_and_keeps_valid_ones()
    {
        // The model returns a bogus "marketing" silo mixed with a valid one,
        // inside a fenced JSON block. E2: the bogus silo is dropped.
        var stub = new StubChatClient(_ =>
            "```json\n{ \"silos\": [\"marketing\", \"hr-policies\"], \"confidence\": 0.8, \"reasoning\": \"hr question\" }\n```");
        var router = new LlmClassifierRouter(stub);

        var decision = await router.RouteAsync("What's the parental leave policy?");

        Assert.Single(decision.Silos);
        Assert.Equal("hr-policies", decision.Silos[0]);
        Assert.DoesNotContain("marketing", decision.Silos);
    }

    [Fact]
    public async Task LlmClassifierRouter_returns_low_confidence_when_all_silos_are_hallucinated()
    {
        var stub = new StubChatClient(_ =>
            "{ \"silos\": [\"marketing\", \"sales\"], \"confidence\": 0.95, \"reasoning\": \"made up\" }");
        var router = new LlmClassifierRouter(stub);

        var decision = await router.RouteAsync("What's the parental leave policy?");

        Assert.Empty(decision.Silos);
        Assert.Equal(0, decision.Confidence);
        Assert.Equal("no valid silos", decision.Reasoning);
    }

    [Fact]
    public async Task MultiSourceRouter_short_circuits_when_rule_based_is_confident()
    {
        var rule = new RuleBasedRouter();
        var fallback = new LlmClassifierRouter(new StubChatClient(_ => "{}")); // Should not be called.
        var router = new MultiSourceRouter(rule, fallback, confidenceThreshold: 0.1);

        var decision = await router.RouteAsync("vacation policy remote work parental leave");

        Assert.Equal("multi(rule-based+llm-classifier)", decision.Strategy);
        Assert.Contains("hr-policies", decision.Silos);

        // The composite Strategy name mentions "llm-classifier" even though no
        // LLM was called. Escalated is what the cost metric counts, and it must
        // stay false here -- inferring escalation from the name reported ~100%
        // escalation on traffic the cheap rung actually absorbed.
        Assert.False(decision.Escalated);
    }

    [Fact]
    public async Task MultiSourceRouter_falls_back_to_llm_classifier_on_low_rule_confidence()
    {
        var rule = new RuleBasedRouter();
        var fallback = new LlmClassifierRouter(new StubChatClient(_ =>
            "{ \"silos\": [\"product-catalog\"], \"confidence\": 0.85, \"reasoning\": \"asks about pricing tier\" }"));
        var router = new MultiSourceRouter(rule, fallback, confidenceThreshold: 0.5);

        var decision = await router.RouteAsync("compare the offerings"); // No rule keywords.

        Assert.Contains("product-catalog", decision.Silos);

        // This one really did pay for the LLM rung.
        Assert.True(decision.Escalated);
    }

    [Fact]
    public async Task RuleBasedRouter_alone_never_reports_an_escalation()
    {
        var decision = await new RuleBasedRouter()
            .RouteAsync("vacation policy remote work parental leave");

        Assert.False(decision.Escalated);
    }

    [Fact]
    public async Task LlmClassifierRouter_alone_always_reports_an_escalation()
    {
        // Wired standalone, every query pays for an LLM call -- so 100% here is
        // the correct reading, not the bug the composite name used to produce.
        var router = new LlmClassifierRouter(new StubChatClient(_ =>
            "{ \"silos\": [\"hr-policies\"], \"confidence\": 0.9, \"reasoning\": \"x\" }"));

        var decision = await router.RouteAsync("anything");

        Assert.True(decision.Escalated);
    }

    [Fact]
    public async Task ConversationalQueryRewriter_resolves_followup_against_history()
    {
        var chat = new StubChatClient(p =>
            p.Contains("And in Paris", StringComparison.Ordinal)
                ? "What's the vacation policy in the Paris office?"
                : "Should not happen.");
        var rewriter = new ConversationalQueryRewriter(chat);

        var history = new[]
        {
            ("user", "What's our vacation policy?"),
            ("assistant", "Employees receive 20 paid vacation days per year."),
        };

        var rewritten = await rewriter.RewriteAsync(history, "And in Paris?");

        Assert.Equal("What's the vacation policy in the Paris office?", rewritten);
    }

    [Fact]
    public async Task ConversationalQueryRewriter_returns_input_unchanged_when_no_history()
    {
        var chat = new StubChatClient(_ => "should not be called");
        var rewriter = new ConversationalQueryRewriter(chat);

        var rewritten = await rewriter.RewriteAsync([], "What's our vacation policy?");

        Assert.Equal("What's our vacation policy?", rewritten);
    }

    [Fact]
    public async Task ConversationalQueryRewriter_only_includes_last_N_turns_in_prompt()
    {
        string? capturedPrompt = null;
        var chat = new StubChatClient(p =>
        {
            capturedPrompt = p;
            return "rewritten";
        });
        var rewriter = new ConversationalQueryRewriter(chat, maxHistoryTurns: 2);

        // Six turns of history; only the final two should reach the prompt.
        var history = new[]
        {
            ("user", "turn-one-oldest"),
            ("assistant", "turn-two"),
            ("user", "turn-three"),
            ("assistant", "turn-four"),
            ("user", "turn-five"),
            ("assistant", "turn-six-newest"),
        };

        await rewriter.RewriteAsync(history, "the follow-up");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("turn-five", capturedPrompt, StringComparison.Ordinal);
        Assert.Contains("turn-six-newest", capturedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-one-oldest", capturedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-four", capturedPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SemanticRouter_routes_vacation_query_to_hr_policies_by_embedding()
    {
        var router = new SemanticRouter(BagOfWordsService());

        var decision = await router.RouteAsync("What is the vacation policy?");

        Assert.Equal("semantic-embedding", decision.Strategy);
        Assert.Contains("hr-policies", decision.Silos);
        Assert.True(decision.Confidence is > 0 and <= 1.0);
    }

    [Fact]
    public async Task InstrumentedRouter_records_confidence_and_silo_instruments()
    {
        using var metrics = new RoutingMetrics();
        var rule = new RuleBasedRouter();
        var router = new InstrumentedRouter(rule, metrics);

        var recordedConfidence = false;
        long siloCount = 0;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RoutingMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
        {
            if (instrument.Name == "smartdocs.routing.confidence")
            {
                recordedConfidence = true;
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
        {
            if (instrument.Name == "smartdocs.routing.routes")
            {
                Interlocked.Add(ref siloCount, value);
            }
        });
        listener.Start();

        var decision = await router.RouteAsync("vacation policy remote work parental leave");
        listener.Dispose();

        Assert.NotEmpty(decision.Silos);
        Assert.True(recordedConfidence, "confidence histogram should have fired");
        Assert.True(siloCount >= 1, "silo counter should have fired at least once");
    }
}
