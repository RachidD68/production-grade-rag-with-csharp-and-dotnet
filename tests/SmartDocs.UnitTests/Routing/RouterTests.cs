using SmartDocs.Routing;

namespace SmartDocs.UnitTests.Routing;

public sealed class RouterTests
{
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
    public async Task SemanticRouter_parses_LLM_classification_json()
    {
        var stub = new StubChatClient(_ =>
            "{ \"silos\": [\"financial-reports\"], \"confidence\": 0.9, \"reasoning\": \"asks about Q3 revenue\" }");
        var router = new SemanticRouter(stub);

        var decision = await router.RouteAsync("What was Q3 revenue?");

        Assert.Single(decision.Silos);
        Assert.Equal("financial-reports", decision.Silos[0]);
        Assert.Equal(0.9, decision.Confidence);
    }

    [Fact]
    public async Task MultiSourceRouter_short_circuits_when_rule_based_is_confident()
    {
        var rule = new RuleBasedRouter();
        var semantic = new SemanticRouter(new StubChatClient(_ => "{}")); // Should not be called.
        var router = new MultiSourceRouter(rule, semantic, confidenceThreshold: 0.1);

        var decision = await router.RouteAsync("vacation policy remote work parental leave");

        Assert.Equal("multi(rule-based+semantic)", decision.Strategy);
        Assert.Contains("hr-policies", decision.Silos);
    }

    [Fact]
    public async Task MultiSourceRouter_falls_back_to_semantic_on_low_rule_confidence()
    {
        var rule = new RuleBasedRouter();
        var semantic = new SemanticRouter(new StubChatClient(_ =>
            "{ \"silos\": [\"product-catalog\"], \"confidence\": 0.85, \"reasoning\": \"asks about pricing tier\" }"));
        var router = new MultiSourceRouter(rule, semantic, confidenceThreshold: 0.5);

        var decision = await router.RouteAsync("compare the offerings"); // No rule keywords.

        Assert.Contains("product-catalog", decision.Silos);
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
}
