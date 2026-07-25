using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace RagInDotNet.Samples.Ch18_McpServer;

/// <summary>
/// Illustrative MCP <em>client</em> using the Microsoft Agent Framework (MAF)
/// AIAgent idiom — the path a host application takes to consume this server's
/// tools. It is compiled (so the API surface is verified against the pinned
/// ModelContextProtocol 1.4.0 + Microsoft.Agents.AI 1.14.0) but never invoked by
/// <c>Program</c>: <see cref="RunAsync"/> needs a real <see cref="IChatClient"/>
/// (OpenAI / Azure / Anthropic), so it cannot run offline.
/// <para>
/// Verified API surface (1.4.0 / 1.14.0):
/// <list type="bullet">
///   <item><c>McpClient.CreateAsync(IClientTransport, ...)</c> — note: <c>McpClientFactory</c> no longer exists.</item>
///   <item><c>StdioClientTransport(StdioClientTransportOptions, ILoggerFactory?)</c>.</item>
///   <item><c>client.ListToolsAsync()</c> returns <c>IList&lt;McpClientTool&gt;</c>; <c>McpClientTool : AIFunction : AITool</c>.</item>
///   <item><c>chatClient.AsAIAgent(instructions, name, description, tools, ...)</c> returns a <c>ChatClientAgent : AIAgent</c>.</item>
///   <item><c>agent.RunAsync(string message, ...)</c> returns <c>Task&lt;AgentResponse&gt;</c>.</item>
/// </list>
/// </para>
/// <para>
/// FOOTGUN: against a Streamable-HTTP server, configure the client transport
/// with <c>HttpTransportMode.StreamableHttp</c> (or <c>AutoDetect</c>). A client
/// pinned to <c>HttpTransportMode.Sse</c> talking to a Streamable-HTTP server
/// gets an EMPTY tool list from <c>ListToolsAsync()</c>, and the agent then
/// hallucinates tool calls instead of failing loudly. Match client mode to the
/// server.
/// </para>
/// </summary>
internal static class SmartDocsMcpClient
{
    /// <summary>
    /// Connect to the SmartDocs stdio server, list its tools, and hand them to a
    /// MAF agent so the model can call them. Requires a live chat client.
    /// </summary>
    public static async Task<string> RunAsync(IChatClient chatClient, string userQuery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(userQuery);

        // 1. Launch this server as a child process over stdio.
        await using var mcp = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "SmartDocs",
                Command = "dotnet",
                Arguments = ["run", "--project", "samples/Ch18_McpServer"],
            }),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // 2. Discover the tools. McpClientTool already IS an AITool — no adapter.
        IList<McpClientTool> mcpTools = await mcp.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        // 3. Build a MAF agent over any IChatClient, handing it the MCP tools.
        AIAgent agent = chatClient.AsAIAgent(
            instructions: "Answer using the SmartDocs tools; cite chunk ids.",
            tools: [.. mcpTools.Cast<AITool>()]);

        // 4. Run. The agent decides when to call search / get_chunk / etc.
        var response = await agent.RunAsync(userQuery, cancellationToken: cancellationToken).ConfigureAwait(false);
        return response.Text;
    }

    // For a remote Streamable-HTTP server, swap the transport (note the mode):
    //   await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
    //   {
    //       Endpoint = new Uri("https://smartdocs.example.com/mcp"),
    //       TransportMode = HttpTransportMode.StreamableHttp,   // NOT Sse
    //   }));
}
