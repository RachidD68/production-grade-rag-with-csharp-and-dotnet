// Ch 20 — Ground-truth evaluation, the framework-native way (MAF 1.14).
//
// The Microsoft Agent Framework (available in MAF 1.14, introduced earlier in
// the 1.x line) ships a real evaluation framework in the Microsoft.Agents.AI
// namespace: EvalItem (a query + the agent's response + an optional
// ExpectedOutput), reusable EvalChecks, FunctionEvaluator.Create(...) for your
// own pass/fail predicates, and LocalEvaluator — an offline evaluator that runs
// those checks with NO second LLM and no API calls, which is exactly what you
// want in CI.
//
// This sample runs a tiny SmartDocs HR agent against a 10-question gold set,
// turns each (question, answer, expected-substring) triple into an EvalItem,
// and scores them with a LocalEvaluator built from two checks:
//   • a custom FunctionEvaluator "expected_substring" check, and
//   • a built-in EvalChecks.NonEmpty() check.
// LocalEvaluator returns an AgentEvaluationResults with Passed / Failed / Total
// and AllPassed — the aggregate the CI gate reads.
//
// Everything is offline and deterministic: the agent is backed by a
// CannedAnswersClient stub (no model, no key), and LocalEvaluator never calls a
// model. The bare foreach further down is kept only as a labeled teaching aside
// that shows what LocalEvaluator does under the hood — it is NOT the recommended
// path.
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

// ── Step 1: run the agent over the gold set, collecting EvalItems. ───────────
// An EvalItem carries the query, the agent's actual response, and the expected
// output (the gold substring) the checks will assert against.
var items = new List<EvalItem>(gold.Length);
foreach (var (question, expected) in gold)
{
    var session = await agent.CreateSessionAsync();
    var run = await agent.RunAsync(question, session);
    items.Add(new EvalItem(query: question, response: run.Text ?? string.Empty)
    {
        ExpectedOutput = expected,
    });
}

// ── Step 2: define the checks. ───────────────────────────────────────────────
// A FunctionEvaluator wraps a predicate as a reusable EvalCheck. The
// (response, expectedOutput) overload receives the EvalItem's ExpectedOutput as
// the second argument — perfect for a ground-truth substring match.
var expectedSubstring = FunctionEvaluator.Create(
    "expected_substring",
    (string response, string? expectedOutput) =>
        expectedOutput is not null &&
        response.Contains(expectedOutput, StringComparison.OrdinalIgnoreCase));

// EvalChecks ships ready-made checks; NonEmpty() guards against an agent that
// silently returns an empty string (which a naive substring test would miss
// when the expected value is itself empty).
var nonEmpty = EvalChecks.NonEmpty();

// ── Step 3: run the LocalEvaluator — offline, no LLM in the loop. ────────────
var evaluator = new LocalEvaluator(expectedSubstring, nonEmpty);
var results = await evaluator.EvaluateAsync(items, evalName: "Ch20 Ground-Truth Eval");

Console.WriteLine("=== Ch20: Ground-truth eval via MAF LocalEvaluator (offline) ===");
Console.WriteLine();
Console.WriteLine(
    $"LocalEvaluator: {results.Passed}/{results.Total} item-checks passed " +
    $"(Failed={results.Failed}, AllPassed={results.AllPassed}).");
Console.WriteLine();

// Per-question view: re-run the expected_substring predicate for display so the
// reader sees which gold answer each item matched (LocalEvaluator's aggregate
// does not surface per-item detail in this build).
Console.WriteLine("Per-question result (expected_substring check):");
int substringPassed = 0;
foreach (var item in items)
{
    var pass = item.Response.Contains(item.ExpectedOutput!, StringComparison.OrdinalIgnoreCase);
    if (pass)
    {
        substringPassed++;
    }
    var status = pass ? "PASS" : "FAIL";
    Console.WriteLine($"  [{status}] {item.Query}");
    Console.WriteLine($"         expected substring: \"{item.ExpectedOutput}\"");
    if (!pass)
    {
        Console.WriteLine($"         got answer: \"{item.Response}\"");
    }
}
Console.WriteLine();
Console.WriteLine($"Ground-truth match: {substringPassed}/{gold.Length} " +
                  $"({100.0 * substringPassed / gold.Length:F0}%).");

// ── Aside: what LocalEvaluator does under the hood. ──────────────────────────
// This is the bare loop the chapter used to present as the eval. It is NOT the
// recommended approach — it reinvents, by hand, exactly the pass/fail
// aggregation that LocalEvaluator + FunctionEvaluator give you for free above.
// Shown only so the framework call is demystified, not mysterious.
Console.WriteLine();
Console.WriteLine("(aside) The same check expressed as a hand-rolled loop — what the");
Console.WriteLine("        framework does internally; prefer LocalEvaluator in real code:");
int manualPassed = 0;
foreach (var (question, expected) in gold)
{
    var session = await agent.CreateSessionAsync();
    var run = await agent.RunAsync(question, session);
    var text = run.Text ?? string.Empty;
    if (text.Contains(expected, StringComparison.OrdinalIgnoreCase))
    {
        manualPassed++;
    }
}
Console.WriteLine($"        hand-rolled loop agrees: {manualPassed}/{gold.Length} passed.");

return results.AllPassed && substringPassed == gold.Length ? 0 : 1;


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
