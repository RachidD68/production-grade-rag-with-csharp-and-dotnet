// Ch 20 — Ground-truth evaluation
//
// MAF ships first-class workflow evaluation against expected outputs.
// This sample runs a tiny SmartDocs agent against a 10-question gold set
// and reports an aggregate match score plus per-question pass / fail.
//
// The eval shape here is intentionally simple — a literal-and-fuzzy match
// over expected substrings — so the orchestration is visible. In a production
// eval you'd plug in an LLM-as-judge or a structured-output validator; the
// loop, the gold set, and the result aggregation are the parts that don't
// change between styles.
//
// Run: dotnet run --project samples/Ch20_GroundTruthEval

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

// 10-question gold set: question + expected substring that must appear in the answer.
var gold = new (string Question, string ExpectedSubstring)[]
{
    ("How many vacation days do full-time employees get?", "20"),
    ("How far ahead must vacation requests be submitted?", "two weeks"),
    ("Who must approve vacation requests longer than 10 days?", "HR"),
    ("Can unused vacation days be carried over?", "5"),
    ("What system are requests submitted through?", "HR portal"),
    ("What is the company name?", "Contoso"),
    ("What office is the policy from?", "Montreal"),
    ("What's the document title?", "Vacation"),
    ("What fiscal year does this policy apply to?", "2026"),
    ("What's the confidentiality level?", "Internal"),
};

var chat = new CannedAnswersClient();
var agent = new ChatClientAgent(
    chatClient: chat,
    name: "SmartDocsEvalAgent",
    description: "Answers Contoso HR questions for evaluation.",
    instructions: "Answer concisely from the Contoso HR policy. Cite [Source 1] when relevant.");

int passed = 0;
var perQuestion = new List<(string Q, string Expected, string Answer, bool Pass)>();

foreach (var (q, expected) in gold)
{
    var session = await agent.CreateSessionAsync();
    var result = await agent.RunAsync(q, session);
    var text = result.Text ?? string.Empty;
    var pass = text.Contains(expected, StringComparison.OrdinalIgnoreCase);
    if (pass)
    {
        passed++;
    }
    perQuestion.Add((q, expected, text, pass));
}

Console.WriteLine($"Ground-truth eval: {passed}/{gold.Length} passed " +
                  $"({100.0 * passed / gold.Length:F0}%)");
Console.WriteLine();
Console.WriteLine("Per-question result:");
foreach (var (q, expected, answer, pass) in perQuestion)
{
    var status = pass ? "PASS" : "FAIL";
    Console.WriteLine($"  [{status}] {q}");
    Console.WriteLine($"         expected substring: \"{expected}\"");
    if (!pass)
    {
        Console.WriteLine($"         got answer: \"{answer}\"");
    }
}

return passed == gold.Length ? 0 : 1;


// ── Stubs ─────────────────────────────────────────────────────────────────

sealed class CannedAnswersClient : IChatClient
{
    private static readonly Dictionary<string, string> Answers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["How many vacation days do full-time employees get?"] =
            "Full-time employees accrue 20 vacation days per calendar year [Source 1].",
        ["How far ahead must vacation requests be submitted?"] =
            "Vacation requests must be submitted at least two weeks in advance through the HR portal [Source 1].",
        ["Who must approve vacation requests longer than 10 days?"] =
            "Requests longer than 10 consecutive days require both manager approval and HR review [Source 1].",
        ["Can unused vacation days be carried over?"] =
            "Yes, unused days carry over up to 5 days into the next year [Source 1].",
        ["What system are requests submitted through?"] =
            "Vacation requests are submitted through the HR portal [Source 1].",
        ["What is the company name?"] =
            "The policy applies to Contoso employees [Source 1].",
        ["What office is the policy from?"] =
            "The Montreal office's HR team published the policy [Source 1].",
        ["What's the document title?"] =
            "The document is titled 'Vacation Policy' [Source 1].",
        ["What fiscal year does this policy apply to?"] =
            "The policy applies to fiscal year 2026 [Source 1].",
        ["What's the confidentiality level?"] =
            "This policy is classified Internal [Source 1].",
    };

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var q = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var answer = Answers.TryGetValue(q, out var canned)
            ? canned
            : "I don't know based on the available sources.";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
