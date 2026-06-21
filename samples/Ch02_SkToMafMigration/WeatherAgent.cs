using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch02_SkToMafMigration;

/// <summary>
/// The MAF "after" of the migration sample. Lifted out of <c>Program.cs</c>
/// so the unit test in <c>SmartDocs.UnitTests</c> can exercise it with a
/// stub <see cref="IChatClient"/>.
/// </summary>
internal static class WeatherAgent
{
    /// <summary>
    /// The single tool the weather agent exposes. In Semantic Kernel this
    /// would have been a method decorated with <c>[KernelFunction]</c>; in
    /// MAF we wrap a plain delegate with <c>AIFunctionFactory.Create</c>
    /// — no attribute, no plugin object, no <c>kernel.Plugins.AddFromObject</c>.
    /// </summary>
    public static AIFunction CreateGetWeatherTool()
    {
        return AIFunctionFactory.Create(
            (string city) => $"It's sunny and 22°C in {city}.",
            name: "get_weather",
            description: "Returns the current weather for the named city.");
    }

    /// <summary>
    /// Build a one-shot weather agent and ask it <paramref name="question"/>.
    /// Mirrors the Semantic-Kernel-equivalent flow shown in the rename map
    /// at the top of <c>Program.cs</c>.
    /// </summary>
    public static async Task<string> AskAsync(
        IChatClient chat,
        string question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var weatherTool = CreateGetWeatherTool();

        // The MAF 1.10 ChatClientAgent constructor takes name / description /
        // instructions / tools positionally — no plugin object, no
        // [KernelFunction] attribute on a method. The chat client itself is
        // the only required dependency.
        var agent = new ChatClientAgent(
            chatClient: chat,
            name: "Weatherman",
            description: null,
            instructions: "You are a concise weather assistant. " +
                          "Use the get_weather tool when asked about a city.",
            tools: [weatherTool]);

        // SK's `new ChatHistory(...)` becomes `agent.CreateSessionAsync(...)`.
        // The session carries conversation state across turns; for a one-shot
        // call we just create + use + dispose.
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(question, session, options: null, cancellationToken: cancellationToken);

        return response.Text ?? string.Empty;
    }
}

/* =============================================================================
   Reference: the Semantic Kernel "Before" version of the same agent.
   This block is intentionally a comment — it is not compiled. Copy it into a
   project that references Microsoft.SemanticKernel to verify the equivalence.

   using Microsoft.SemanticKernel;
   using Microsoft.SemanticKernel.ChatCompletion;
   using System.ComponentModel;

   public sealed class WeatherPlugin
   {
       [KernelFunction("get_weather")]
       [Description("Returns the current weather for the named city.")]
       public string GetWeather([Description("The city name.")] string city)
           => $"It's sunny and 22°C in {city}.";
   }

   public static async Task<string> AskAsync_Sk(string apiKey, string deployment, string endpoint, string question)
   {
       var kernel = Kernel.CreateBuilder()
           .AddAzureOpenAIChatCompletion(deployment, endpoint, apiKey)
           .Build();
       kernel.Plugins.AddFromObject(new WeatherPlugin());

       var chat = kernel.GetRequiredService<IChatCompletionService>();
       var history = new ChatHistory("You are a concise weather assistant. Use the get_weather function when asked about a city.");
       history.AddUserMessage(question);

       var settings = new OpenAIPromptExecutionSettings
       {
           ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
       };
       var result = await chat.GetChatMessageContentAsync(history, settings, kernel);
       return result.Content ?? string.Empty;
   }
   ============================================================================= */
