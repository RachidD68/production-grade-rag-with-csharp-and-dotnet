// Ch 23 — Human-in-the-loop tool approval (MAF 1.20.0)
//
// Demonstrates the Microsoft Agent Framework human-in-the-loop approval flow for
// a *state-changing* tool. Two tools are exposed:
//   • get_document   — a benign read; auto-approved.
//   • delete_document — a destructive write; wrapped in ApprovalRequiredAIFunction
//                       so the agent pauses and asks a human before running it.
//
// API note (verified against the installed assemblies):
//   The approval surface lives in Microsoft.Extensions.AI, NOT in
//   Microsoft.Agents.AI:
//     - Microsoft.Extensions.AI.ApprovalRequiredAIFunction wraps any AIFunction.
//     - The agent surfaces a Microsoft.Extensions.AI.ToolApprovalRequestContent
//       (carrying the FunctionCallContent) inside AgentResponse.Messages.
//     - The host calls request.CreateResponse(approved, reason) to get a
//       ToolApprovalResponseContent, then feeds it back into the next RunAsync.
//   The chapter prose calls this "ToolApprovalAgent / FunctionApprovalRequest";
//   the shipped 1.10.0 types are the ApprovalRequired* / ToolApproval* names
//   used below.
//
// Runs fully offline with a stub IChatClient — no model or network. Exit 0.
//
// Run: dotnet run --project samples/Ch23_HumanInTheLoopApproval

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// Both tools are approval-gated here so the demo shows the human APPROVING the
// benign read and DENYING the destructive delete. In production you would gate
// only state-changing tools; the read could run un-gated.
var getDocument = new ApprovalRequiredAIFunction(
    AIFunctionFactory.Create(
        (string documentId) => $"Document '{documentId}': Q3 financial summary (read-only).",
        name: "get_document",
        description: "Reads a document by id. Safe, read-only."));

// The destructive tool is wrapped so the framework requires explicit approval
// before it is ever invoked.
var deleteDocument = new ApprovalRequiredAIFunction(
    AIFunctionFactory.Create(
        (string documentId) => $"Document '{documentId}' permanently deleted.",
        name: "delete_document",
        description: "Permanently deletes a document by id. State-changing and irreversible."));

var chat = new ScriptedToolCallChatClient();

var agent = new ChatClientAgent(
    chatClient: chat,
    name: "DocOpsAgent",
    description: "Performs document operations with human approval for destructive actions.",
    instructions: "Use get_document to read and delete_document to delete. Always confirm deletes with a human.",
    tools: [getDocument, deleteDocument]);

Console.WriteLine("Human-in-the-loop tool approval demo (offline).");
Console.WriteLine();

// Turn 1: a benign READ — the console auto-approves any approval request.
await RunWithApprovalAsync(
    agent, chat, ScriptedToolCallChatClient.ReadInstruction,
    "Read document fin-q3.", autoApprove: true);

Console.WriteLine();

// Turn 2: a destructive DELETE — the console DENIES the approval request.
await RunWithApprovalAsync(
    agent, chat, ScriptedToolCallChatClient.DeleteInstruction,
    "Delete document fin-q3.", autoApprove: false);

Console.WriteLine();
Console.WriteLine("Done.");
return 0;


// Drives one request, then satisfies any pending tool-approval requests by
// approving or denying them, and prints the outcome.
static async Task RunWithApprovalAsync(
    ChatClientAgent agent,
    ScriptedToolCallChatClient chat,
    string scriptedMode,
    string userMessage,
    bool autoApprove)
{
    chat.Mode = scriptedMode;
    Console.WriteLine($"User: {userMessage}");

    var session = await agent.CreateSessionAsync();
    var response = await agent.RunAsync(userMessage, session);

    var approvalRequests = response.Messages
        .SelectMany(m => m.Contents)
        .OfType<ToolApprovalRequestContent>()
        .ToList();

    if (approvalRequests.Count == 0)
    {
        // No approval gate hit (e.g. a tool that does not require approval ran
        // straight through to a final answer).
        Console.WriteLine($"Agent: {response.Text}");
        return;
    }

    // Build one approval response per request and feed them back as a follow-up
    // user turn on the SAME session — the framework either runs the approved
    // tool or skips the denied one.
    var replies = new List<AIContent>();
    foreach (var request in approvalRequests)
    {
        var toolName = (request.ToolCall as FunctionCallContent)?.Name ?? "<tool>";
        var decision = autoApprove ? "APPROVED" : "DENIED";
        Console.WriteLine($"  Approval requested for '{toolName}' -> {decision}");

        replies.Add(request.CreateResponse(
            approved: autoApprove,
            reason: autoApprove ? "Read is safe." : "Human declined the destructive action."));
    }

    if (!autoApprove)
    {
        // Denied: the destructive tool never runs.
        Console.WriteLine("Agent: deletion blocked — no document was deleted.");
        return;
    }

    var followUp = await agent.RunAsync(new ChatMessage(ChatRole.User, replies), session);
    Console.WriteLine($"Agent: {followUp.Text}");
}


// ── Offline stub: scripts a single tool call per turn. ──────────────────────
// On the read turn it calls get_document directly; on the delete turn it calls
// the approval-gated delete_document, which the framework converts into a
// ToolApprovalRequestContent before the function ever runs.
sealed class ScriptedToolCallChatClient : IChatClient
{
    public const string ReadInstruction = "read";
    public const string DeleteInstruction = "delete";

    public string Mode { get; set; } = ReadInstruction;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // If a tool has already produced a result this turn, the model would now
        // compose a final answer rather than call another tool.
        var sawToolResult = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Any();

        if (sawToolResult)
        {
            var summary = messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .Last()
                .Result?.ToString() ?? "Operation complete.";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, summary)));
        }

        var name = Mode == DeleteInstruction ? "delete_document" : "get_document";
        var call = new FunctionCallContent(
            callId: Guid.NewGuid().ToString("N"),
            name: name,
            arguments: new Dictionary<string, object?> { ["documentId"] = "fin-q3" });

        var message = new ChatMessage(ChatRole.Assistant, [call]);
        return Task.FromResult(new ChatResponse(message));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Streaming not used in this sample.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
