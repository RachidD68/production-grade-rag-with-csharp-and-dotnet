using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;

namespace RagInDotNet.Samples.Ch09_RerankingEval;

/// <summary>
/// A small, deterministic SmartDocs/Contoso corpus and a gold query set for the
/// Chapter 9 reranking comparison. Fifteen single-chunk documents cover the
/// usual internal-policy ground (vacation, remote work, expenses, security,
/// and so on); each document's leading sentence states its topic so retrieval
/// has a fair target.
///
/// <para>
/// The gold queries are phrased in the user's words and share enough vocabulary
/// with several documents that the offline bag-of-words dense retriever pulls
/// the right document into the top-5 but often at a middling rank. That is the
/// setup the chapter wants: recall@5 is already healthy, so a good reranker
/// improves the rank-sensitive metrics (nDCG@5, MRR) far more than recall@5.
/// </para>
/// </summary>
public static class Corpus
{
    /// <summary>How many results each query retrieves; metrics are measured at this K.</summary>
    public const int GoldK = 5;

    /// <summary>Fifteen single-chunk SmartDocs/Contoso policy documents.</summary>
    public static IReadOnlyList<DocumentChunk> BuildChunks()
    {
        var specs = new (string DocId, string Dept, string Title, string Text)[]
        {
            ("vacation", "HR", "Annual Vacation Policy",
                "Vacation policy: employees accrue twenty paid vacation days per year, earned monthly. " +
                "Unused vacation days roll over up to a maximum of ten into the next year. " +
                "Vacation requests longer than ten consecutive days require director approval."),
            ("remote", "HR", "Remote Work Policy",
                "Remote work policy: employees may work from home up to three days per week with manager approval. " +
                "Remote workers stay available during core hours and connect through the company VPN. " +
                "A fully remote arrangement needs an exception approved by the department head."),
            ("expenses", "Finance", "Travel Expense Reimbursement",
                "Expense reimbursement: business travel expenses are repaid when submitted with itemized receipts. " +
                "Daily meal allowances are capped at seventy-five dollars while traveling on company business. " +
                "Airfare must be booked in economy class unless the flight exceeds six hours."),
            ("parental", "HR", "Parental Leave Policy",
                "Parental leave: new parents receive sixteen weeks of fully paid leave after a birth or adoption. " +
                "Leave may be taken any time within the first twelve months. " +
                "Notify HR at least thirty days before the intended leave start date."),
            ("sick", "HR", "Sick Leave Policy",
                "Sick leave: paid sick leave is unlimited for employees in good standing. " +
                "Notify your manager within twenty-four hours of taking a sick day. " +
                "Absences beyond ten consecutive days require a medical certificate."),
            ("stipend", "HR", "Home Office Equipment Stipend",
                "Home office stipend: the equipment allowance is fifteen hundred dollars per year. " +
                "Approved items include monitors, keyboards, ergonomic chairs, and standing desks. " +
                "Submit receipts through the expense portal within thirty days of purchase."),
            ("vpn", "Engineering", "VPN Access Runbook",
                "VPN access: connect to the corporate VPN before reaching any internal service or database. " +
                "Install the WireGuard client and import the configuration profile from the IT portal. " +
                "If the tunnel drops, rotate your access key and reconnect through the security console."),
            ("oncall", "Engineering", "On-Call Escalation Runbook",
                "On-call escalation: the on-call engineer acknowledges every page within fifteen minutes. " +
                "Escalate to the secondary responder if an incident is not contained within thirty minutes. " +
                "Post a public status update for any customer-facing outage lasting over five minutes."),
            ("deploy", "Engineering", "Deployment Pipeline",
                "Deployment pipeline: releases run through a blue-green pipeline with automated smoke tests. " +
                "A canary fleet receives five percent of traffic before a full rollout. " +
                "Rollback is a single command that repoints the load balancer to the previous release."),
            ("secrets", "Engineering", "Secrets Management",
                "Secrets management: all credentials live in the managed key vault and are injected at runtime. " +
                "Service identities authenticate with short-lived tokens that expire after one hour. " +
                "Rotate any leaked secret immediately and audit its access history."),
            ("expenses-card", "Finance", "Corporate Card Policy",
                "Corporate card: the company card is for approved business expenses only, never personal spending. " +
                "Reconcile every card charge with a receipt in the expense portal by month end. " +
                "Lost or stolen cards must be reported to Finance within one business day."),
            ("payroll", "Finance", "Payroll Schedule",
                "Payroll schedule: salaries are paid on the last business day of each month by direct deposit. " +
                "Timesheet corrections are submitted three business days before the payroll run. " +
                "Year-end tax documents are issued by the end of January."),
            ("nda", "Legal", "Non-Disclosure Agreement Terms",
                "Non-disclosure agreement: the mutual NDA binds both parties for a term of five years. " +
                "Confidential information excludes anything already public or independently developed. " +
                "A breach entitles the disclosing party to seek injunctive relief."),
            ("sla", "Legal", "Service Level Agreement",
                "Service level agreement: the SLA guarantees ninety-nine point nine percent monthly uptime. " +
                "Service credits apply when availability falls below the committed threshold. " +
                "Scheduled maintenance windows are excluded from the uptime calculation."),
            ("conduct", "HR", "Code of Conduct",
                "Code of conduct: harassment, discrimination, and retaliation are strictly prohibited. " +
                "Conflicts of interest must be disclosed to your manager in writing. " +
                "Violations may be reported anonymously through the ethics hotline."),
        };

        var chunks = new List<DocumentChunk>(specs.Length);
        foreach (var (docId, dept, title, text) in specs)
        {
            var meta = new DocumentMetadata(
                Id: docId,
                Silo: "smartdocs",
                Department: dept,
                Office: "Contoso HQ",
                ConfidentialityLevel: "Internal",
                DocumentType: "Policy",
                FiscalYear: 2026,
                Author: "SmartDocs",
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
    /// Ten gold (query → expected document) pairs, phrased in the user's words.
    /// </summary>
    public static IReadOnlyList<GoldenItem> BuildGoldQueries()
    {
        (string Query, string ExpectedDocId)[] pairs =
        [
            // Each query is phrased to share surface vocabulary with a sibling
            // document, so the offline bag-of-words retriever often pulls a
            // plausible-but-wrong neighbor to the top while still keeping the
            // right document inside the top-5. Recall@5 therefore stays high
            // across arms; the topical reranker's job is to push the right
            // document up to rank 1, which is what nDCG@5 and MRR reward.
            ("How many paid vacation days do I get and can they roll over?", "vacation"),
            ("How many days a week can I work from home remotely?",          "remote"),
            ("What receipt do I submit to get travel meal expenses reimbursed?", "expenses"),
            ("How much paid leave do new parents get after a birth?",        "parental"),
            ("When do I need a medical certificate for sick leave?",         "sick"),
            ("What yearly allowance covers monitors and a standing desk at home?", "stipend"),
            ("Through what do remote workers connect to reach internal systems?", "vpn"),
            ("How fast must the on-call engineer acknowledge a page?",       "oncall"),
            ("Where are runtime credentials and access tokens kept?",        "secrets"),
            ("What monthly uptime does the agreement guarantee with credits?", "sla"),
        ];

        return pairs
            .Select(p => new GoldenItem(
                Query: p.Query,
                ExpectedDocumentIds: new HashSet<string>(StringComparer.Ordinal) { p.ExpectedDocId }))
            .ToList();
    }
}
