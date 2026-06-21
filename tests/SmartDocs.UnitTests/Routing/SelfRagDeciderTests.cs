using SmartDocs.Routing;

namespace SmartDocs.UnitTests.Routing;

public sealed class SelfRagDeciderTests
{
    [Fact]
    public async Task DecideAsync_general_knowledge_question_returns_false()
    {
        // A general-knowledge question: the model says retrieval is unnecessary.
        var stub = new StubChatClient(_ =>
            "{\"shouldRetrieve\":false,\"reasoning\":\"Basic arithmetic is general knowledge.\"}");
        var decider = new SelfRagDecider(stub);

        var decision = await decider.DecideAsync("What is 2 + 2?");

        Assert.False(decision.ShouldRetrieve);
        Assert.Contains("general knowledge", decision.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecideAsync_corpus_specific_question_returns_true()
    {
        var stub = new StubChatClient(_ =>
            "{\"shouldRetrieve\":true,\"reasoning\":\"Asks about an internal policy.\"}");
        var decider = new SelfRagDecider(stub);

        var decision = await decider.DecideAsync("What is Contoso's vacation carryover policy?");

        Assert.True(decision.ShouldRetrieve);
    }

    [Fact]
    public async Task DecideAsync_defaults_to_retrieve_when_response_unparseable()
    {
        // Never throws on a bad response — errs toward grounding.
        var stub = new StubChatClient(_ => "I cannot answer in JSON, sorry.");
        var decider = new SelfRagDecider(stub);

        var decision = await decider.DecideAsync("anything");

        Assert.True(decision.ShouldRetrieve);
    }
}
