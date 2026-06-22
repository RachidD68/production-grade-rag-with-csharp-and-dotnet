namespace RagInDotNet.Samples.Ch20_MeaiEvaluation;

/// <summary>One labelled evaluation case for the SmartDocs HR corpus.</summary>
/// <param name="Question">The user's question.</param>
/// <param name="Answer">The RAG pipeline's grounded answer (the system-under-test output).</param>
/// <param name="RetrievedContext">The chunk texts the retriever surfaced for this question.</param>
/// <param name="ReferenceAnswer">A human-written reference answer, used by the deterministic BLEU pass.</param>
internal sealed record HrEvalCase(
    string Question,
    string Answer,
    IReadOnlyList<string> RetrievedContext,
    string ReferenceAnswer);

/// <summary>
/// A ten-item SmartDocs HR evaluation set: question, the pipeline's answer, the
/// retrieved context that answer was grounded in, and a reference answer. The
/// answers are deliberately a mix — most are well-grounded, one (the parental
/// leave item) introduces a fact absent from its context so the groundedness
/// score has something to bite on.
/// </summary>
internal static class HrEvalSet
{
    public static IReadOnlyList<HrEvalCase> Build() =>
    [
        new("How many vacation days do full-time employees get?",
            "Full-time employees accrue 20 paid vacation days per fiscal year.",
            ["Employees accrue twenty paid vacation days per fiscal year, earned monthly."],
            "Full-time employees get 20 paid vacation days per year."),

        new("Can unused vacation days be carried over?",
            "Yes — unused vacation rolls over up to a maximum of ten days into the next fiscal year.",
            ["Unused vacation days roll over up to a maximum of ten into the next fiscal year."],
            "Unused vacation carries over up to ten days into the next fiscal year."),

        new("How many days a week can I work from home?",
            "Remote work is allowed up to three days per week with prior manager approval.",
            ["Remote work is allowed up to three days per week with prior manager approval."],
            "You can work remotely up to three days a week with manager approval."),

        new("What are the core hours I need to be available?",
            "Core hours are 10 AM to 3 PM in your local time zone.",
            ["Employees must remain available during core hours of 10 AM to 3 PM in their local time zone."],
            "Core hours are 10 AM to 3 PM local time."),

        new("How much parental leave do new parents get?",
            "New parents get twenty-four weeks of fully paid parental leave.", // ungrounded: context says sixteen
            ["Parental leave provides sixteen weeks of fully paid time off for new parents."],
            "New parents get sixteen weeks of fully paid parental leave."),

        new("What is the home office equipment stipend?",
            "The home office equipment stipend is fifteen hundred dollars per year.",
            ["The home office equipment stipend is fifteen hundred dollars per year."],
            "The home office stipend is $1,500 per year."),

        new("How much is the professional development budget?",
            "Each employee gets an annual professional development budget of two thousand dollars.",
            ["Each employee receives an annual professional development budget of two thousand dollars."],
            "The professional development budget is $2,000 per year."),

        new("What is the employee referral bonus?",
            "The employee referral bonus is three thousand dollars per successful hire.",
            ["The employee referral bonus is three thousand dollars per successful hire."],
            "The referral bonus is $3,000 per successful hire."),

        new("When do I need a medical certificate for sick leave?",
            "A medical certificate is required for sick leave beyond ten consecutive days.",
            ["Sick leave beyond ten consecutive days requires a medical certificate."],
            "You need a medical certificate after ten consecutive sick days."),

        new("Where can I report harassment anonymously?",
            "You can report harassment anonymously through the ethics hotline.",
            ["Violations may be reported anonymously through the ethics hotline."],
            "Harassment can be reported anonymously via the ethics hotline."),
    ];
}
