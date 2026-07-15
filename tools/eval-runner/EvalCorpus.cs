using Microsoft.Extensions.AI;
using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;

namespace RagInDotNet.Tools.EvalRunner;

/// <summary>One labeled eval case: a query, the documents that answer it, and a reference answer.</summary>
internal sealed record EvalCase(GoldenItem Gold, string ReferenceAnswer);

/// <summary>
/// A small, fixed SmartDocs HR corpus and gold set for the offline eval gate.
/// Twelve single-chunk documents; twelve gold queries, each mapped to the one
/// document that answers it plus a reference answer the faithfulness judge can
/// grade against. Deterministic and self-contained — no dataset files, no model.
/// </summary>
internal static class EvalCorpus
{
    public const int K = 5;

    private static readonly (string DocId, string Title, string Text)[] Specs =
    [
        ("hr-remote", "Remote Work Policy",
            "Remote work is allowed up to three days per week with prior manager approval. " +
            "Employees must remain available during core hours of 10 AM to 3 PM local time."),
        ("hr-vacation", "Annual Vacation Policy",
            "Employees accrue twenty paid vacation days per fiscal year, earned monthly. " +
            "Unused vacation days roll over up to a maximum of ten into the next fiscal year."),
        ("hr-sick", "Sick Leave Policy",
            "Sick leave is unlimited for employees in good standing. " +
            "Sick leave beyond ten consecutive days requires a medical certificate."),
        ("hr-parental", "Parental Leave Policy",
            "Parental leave provides sixteen weeks of fully paid time off for new parents. " +
            "Notify HR at least thirty days before your intended leave start date."),
        ("hr-stipend", "Home Office Equipment Stipend",
            "The home office equipment stipend is fifteen hundred dollars per year. " +
            "Submit receipts through the expense portal within thirty days of purchase."),
        ("hr-training", "Professional Development Budget",
            "Each employee receives an annual professional development budget of two thousand dollars. " +
            "Eligible expenses include conferences, online courses, and certification exams."),
        ("hr-referral", "Employee Referral Bonus",
            "The employee referral bonus is three thousand dollars per successful hire. " +
            "The referred candidate must remain employed for at least ninety days."),
        ("hr-conduct", "Code of Conduct",
            "The code of conduct prohibits harassment, discrimination, and retaliation. " +
            "Violations may be reported anonymously through the ethics hotline."),
        ("hr-expenses", "Travel Expense Reimbursement",
            "Business travel expenses are reimbursed when submitted with itemized receipts. " +
            "Daily meal allowances are capped at seventy-five dollars while traveling."),
        ("hr-payroll", "Payroll Schedule",
            "Salaries are paid on the last business day of each month by direct deposit. " +
            "Year-end tax documents are issued by the end of January."),
        ("hr-security", "VPN Access",
            "Connect to the corporate VPN before reaching any internal service. " +
            "Rotate your access key immediately if the tunnel drops."),
        ("hr-records", "Records Retention",
            "Employment records are kept for the duration of employment plus three years. " +
            "Expired records are destroyed through the certified shredding vendor."),
    ];

    public static IReadOnlyList<DocumentChunk> BuildChunks()
    {
        var chunks = new List<DocumentChunk>(Specs.Length);
        foreach (var (docId, title, text) in Specs)
        {
            var meta = new DocumentMetadata(
                Id: docId, Silo: "hr-policies", Department: "HR", Office: "Montreal",
                ConfidentialityLevel: "Internal", DocumentType: "Policy", FiscalYear: 2026,
                Author: "SmartDocs", LastModified: new DateOnly(2026, 1, 1), Title: title);
            chunks.Add(new DocumentChunk(
                ChunkId: $"{docId}#0", DocumentId: docId, ChunkIndex: 0, Text: text,
                StartCharOffset: 0, EndCharOffset: text.Length, Metadata: meta));
        }
        return chunks;
    }

    public static IReadOnlyList<EvalCase> BuildCases()
    {
        (string Query, string DocId, string Reference)[] pairs =
        [
            ("How many days a week can I work from home?", "hr-remote",
                "You can work remotely up to three days a week with manager approval."),
            ("How many paid vacation days do I get each year?", "hr-vacation",
                "Employees get twenty paid vacation days per fiscal year."),
            ("When do I need a doctor's note for being out sick?", "hr-sick",
                "A medical certificate is required after ten consecutive sick days."),
            ("How much parental leave do new parents get?", "hr-parental",
                "New parents get sixteen weeks of fully paid parental leave."),
            ("What is the budget for a desk and chair at home?", "hr-stipend",
                "The home office stipend is fifteen hundred dollars per year."),
            ("Can the company pay for a certification exam?", "hr-training",
                "The professional development budget of two thousand dollars covers certification exams."),
            ("What is the bonus for referring someone we hire?", "hr-referral",
                "The referral bonus is three thousand dollars per successful hire."),
            ("Where do I report harassment anonymously?", "hr-conduct",
                "Harassment can be reported anonymously through the ethics hotline."),
            ("What is the daily meal limit when I travel for work?", "hr-expenses",
                "The daily meal allowance is capped at seventy-five dollars while traveling."),
            ("When are salaries paid each month?", "hr-payroll",
                "Salaries are paid on the last business day of each month."),
            ("How do I connect to internal services securely?", "hr-security",
                "Connect to the corporate VPN before reaching any internal service."),
            ("How long are employment records kept?", "hr-records",
                "Employment records are kept for the duration of employment plus three years."),
        ];

        return pairs
            .Select(p => new EvalCase(
                new GoldenItem(
                    Query: p.Query,
                    ExpectedDocumentIds: new HashSet<string>(StringComparer.Ordinal) { p.DocId },
                    ReferenceAnswer: p.Reference),
                p.Reference))
            .ToList();
    }
}

/// <summary>
/// A deterministic faithfulness judge for the offline gate. The
/// <see cref="GenerationEvaluator"/> grades each answer sentence against the
/// retrieved context as SUPPORTED / PARTIALLY_SUPPORTED / NOT_SUPPORTED; this
/// stub returns SUPPORTED when the claim's salient noun appears in the context
/// the judge was given, so faithfulness scores are stable run to run.
/// </summary>
internal sealed class DeterministicFaithfulnessJudge : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var prompt = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var split = prompt.IndexOf("Claim:", StringComparison.Ordinal);
        var context = split < 0 ? prompt : prompt[..split];
        var claim = split < 0 ? string.Empty : prompt[(split + "Claim:".Length)..];

        // Treat a claim as supported when most of its content words appear in the
        // context block — a deterministic proxy for "grounded in the sources".
        var contextWords = Tokenize(context);
        var claimWords = Tokenize(claim).ToList();
        var verdict = "PARTIALLY_SUPPORTED";
        if (claimWords.Count > 0)
        {
            var overlap = claimWords.Count(w => contextWords.Contains(w));
            var ratio = (double)overlap / claimWords.Count;
            verdict = ratio >= 0.6 ? "SUPPORTED" : ratio <= 0.2 ? "NOT_SUPPORTED" : "PARTIALLY_SUPPORTED";
        }
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, verdict)));
    }

    private static HashSet<string> Tokenize(string text)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "the", "a", "an", "of", "to", "and", "or", "for", "in", "on", "is", "are", "you", "your", "per", "up", "at", "i", "do" };
        return new HashSet<string>(
            text.ToLowerInvariant()
                .Split([' ', '.', ',', '\n', '\r', '[', ']', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !stop.Contains(w)),
            StringComparer.OrdinalIgnoreCase);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
