using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;

namespace RagInDotNet.Samples.Ch07_IndexingStrategies;

/// <summary>
/// A small, deterministic HR/policy corpus plus a hand-labelled gold query
/// set. Each document is a single chunk (the chapter's strategies project a
/// chunk into one or more embedding inputs, so one chunk per document keeps the
/// fan-out visible). The gold set maps a natural-language query to the
/// <see cref="DocumentMetadata.Id"/> of the document that answers it; the first
/// sentence of every chunk is written to carry the discriminating keyword so
/// that even the first-sentence-only <c>sub-chunk</c> strategy has a fair shot.
/// </summary>
public static class Corpus
{
    /// <summary>How many results each query retrieves; recall is measured at this K.</summary>
    public const int GoldK = 3;

    /// <summary>
    /// Twelve HR/policy chunks, one per document. The leading sentence of each
    /// chunk states the topic outright so the <c>sub-chunk</c> strategy (which
    /// embeds only the first sentence) is not handicapped by buried keywords.
    /// </summary>
    public static IReadOnlyList<DocumentChunk> BuildChunks()
    {
        var specs = new (string DocId, string Title, string Text)[]
        {
            ("hr-remote",
                "Remote Work Policy",
                "Remote work is allowed up to three days per week with prior manager approval. " +
                "Employees must remain available during core hours of 10 AM to 3 PM in their local time zone. " +
                "All remote workers must connect through the company VPN to reach internal systems."),

            ("hr-vacation",
                "Annual Vacation Policy",
                "Employees accrue twenty paid vacation days per fiscal year, earned monthly. " +
                "Unused vacation days roll over up to a maximum of ten into the next fiscal year. " +
                "Vacation requests longer than ten consecutive days require director approval."),

            ("hr-sick",
                "Sick Leave Policy",
                "Sick leave is unlimited for employees in good standing. " +
                "Notify your manager within twenty-four hours of taking a sick day. " +
                "Sick leave beyond ten consecutive days requires a medical certificate."),

            ("hr-stipend",
                "Home Office Equipment Stipend",
                "The home office equipment stipend is fifteen hundred dollars per year. " +
                "Approved items include monitors, keyboards, ergonomic chairs, and standing desks. " +
                "Submit receipts through the expense portal within thirty days of purchase."),

            ("hr-reviews",
                "Performance Review Cadence",
                "Performance reviews are conducted quarterly using the OKR framework. " +
                "Each employee sets three to five objectives at the start of every quarter. " +
                "Managers deliver written feedback within two weeks of the quarter ending."),

            ("hr-parental",
                "Parental Leave Policy",
                "Parental leave provides sixteen weeks of fully paid time off for new parents. " +
                "Leave may be taken any time within the first twelve months after birth or adoption. " +
                "Notify HR at least thirty days before your intended leave start date."),

            ("hr-expenses",
                "Travel Expense Reimbursement",
                "Business travel expenses are reimbursed when submitted with itemised receipts. " +
                "Daily meal allowances are capped at seventy-five dollars while travelling. " +
                "Airfare must be booked in economy class unless a flight exceeds six hours."),

            ("hr-training",
                "Professional Development Budget",
                "Each employee receives an annual professional development budget of two thousand dollars. " +
                "Eligible expenses include conferences, online courses, and certification exams. " +
                "Unused training budget does not carry over to the following year."),

            ("hr-referral",
                "Employee Referral Bonus",
                "The employee referral bonus is three thousand dollars per successful hire. " +
                "The referred candidate must remain employed for at least ninety days. " +
                "Bonuses are paid in the payroll cycle after the ninety-day milestone."),

            ("hr-security",
                "Information Security Policy",
                "All laptops must use full-disk encryption and an approved password manager. " +
                "Report any suspected phishing email to the security team immediately. " +
                "Multi-factor authentication is mandatory for every internal application."),

            ("hr-conduct",
                "Code of Conduct",
                "The code of conduct prohibits harassment, discrimination, and retaliation. " +
                "Conflicts of interest must be disclosed to your manager in writing. " +
                "Violations may be reported anonymously through the ethics hotline."),

            ("hr-onboarding",
                "New Hire Onboarding",
                "New hires complete orientation during their first week of employment. " +
                "IT provisions a laptop and accounts before the official start date. " +
                "A dedicated onboarding buddy is assigned for the first ninety days."),
        };

        var chunks = new List<DocumentChunk>(specs.Length);
        for (var i = 0; i < specs.Length; i++)
        {
            var (docId, title, text) = specs[i];
            var meta = new DocumentMetadata(
                Id: docId,
                Silo: "hr-policies",
                Department: "HR",
                Office: "Montreal",
                ConfidentialityLevel: "Internal",
                DocumentType: "Policy",
                FiscalYear: 2026,
                Author: "People Operations",
                LastModified: new DateOnly(2026, 1, 1),
                Title: title);

            chunks.Add(new DocumentChunk(
                ChunkId: $"{docId}#0",
                DocumentId: docId,
                ChunkIndex: 0,
                Text: text,
                StartCharOffset: 0,
                EndCharOffset: text.Length,
                Metadata: meta));
        }
        return chunks;
    }

    /// <summary>
    /// Gold query → expected-document pairs. Each query is phrased in the user's
    /// words (not the document's), so retrieval has to bridge vocabulary; the
    /// expected document id is the one whose chunk genuinely answers the query.
    /// </summary>
    public static IReadOnlyList<GoldenItem> BuildGoldQueries()
    {
        (string Query, string ExpectedDocId)[] pairs =
        [
            ("How many days a week can I work from home?",            "hr-remote"),
            ("How many paid vacation days do I get each year?",       "hr-vacation"),
            ("What do I do when I am too sick to work?",              "hr-sick"),
            ("How much money is the home office equipment stipend?",  "hr-stipend"),
            ("When do performance reviews happen?",                   "hr-reviews"),
            ("How much parental leave is paid for a new parent?",     "hr-parental"),
            ("What is the daily meal limit when I travel for work?",  "hr-expenses"),
            ("Can the company pay for a certification exam?",         "hr-training"),
            ("What is the bonus for referring someone we hire?",      "hr-referral"),
            ("Do I have to use multi-factor authentication?",         "hr-security"),
        ];

        return pairs
            .Select(p => new GoldenItem(
                Query: p.Query,
                ExpectedDocumentIds: new HashSet<string>(StringComparer.Ordinal) { p.ExpectedDocId }))
            .ToList();
    }
}
