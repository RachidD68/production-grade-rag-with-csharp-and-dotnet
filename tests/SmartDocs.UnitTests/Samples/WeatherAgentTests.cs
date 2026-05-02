using Microsoft.Extensions.AI;
using RagInDotNet.Samples.Ch02_SkToMafMigration;

namespace SmartDocs.UnitTests.Samples;

/// <summary>
/// The Chapter-2 SK→MAF migration sample compiles and runs end-to-end against
/// a stub <see cref="IChatClient"/>. We do not exercise tool-calling here —
/// that requires a real LLM that emits a tool-call response — but we do
/// verify the agent constructs without throwing, runs, and returns the
/// stub's text.
/// </summary>
public sealed class WeatherAgentTests
{
    [Fact]
    public async Task AskAsync_returns_chat_client_response_text()
    {
        // The stub ignores the prompt and just echoes a canned response.
        var chat = new StubChatClient(_ => "It's sunny and 22°C in Paris.");

        var answer = await WeatherAgent.AskAsync(chat, "What's the weather in Paris?");

        Assert.Equal("It's sunny and 22°C in Paris.", answer);
    }

    [Fact]
    public void CreateGetWeatherTool_produces_a_named_AIFunction()
    {
        var tool = WeatherAgent.CreateGetWeatherTool();

        Assert.NotNull(tool);
        Assert.Equal("get_weather", tool.Name);
        Assert.Contains("weather", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_throws_on_null_chat_client()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => WeatherAgent.AskAsync(null!, "hello"));
    }

    [Fact]
    public async Task AskAsync_throws_on_blank_question()
    {
        var chat = new StubChatClient(_ => "ok");
        await Assert.ThrowsAsync<ArgumentException>(
            () => WeatherAgent.AskAsync(chat, "   "));
    }
}
