using SmartDocs.Core.Documents;
using SmartDocs.Evaluation;

namespace RagInDotNet.Samples.Ch08_RetrieverEval;

/// <summary>
/// A deterministic SmartDocs-style corpus and a 50-query gold set for the
/// Chapter 8 dense / sparse / hybrid recall comparison. Thirty documents span
/// five silos (HR, Engineering, Finance, Legal, Product); each document is a
/// single chunk whose leading sentence states the topic outright so retrieval
/// has a fair, language-bridging target.
///
/// <para>
/// The gold set deliberately mixes two query styles so the comparison teaches
/// the real lesson. Some queries are paraphrases that share little surface
/// vocabulary with the document ("time off for a new baby" → the parental-leave
/// doc), which favour the dense leg; others lean on a distinctive keyword
/// ("VPN", "HNSW", "FreshBooks") that BM25's exact-match scoring rewards. Fused
/// retrieval is expected to recover the union — at or above the better single
/// leg on every query — which is why hybrid tops the table.
/// </para>
/// </summary>
public static class Corpus
{
    /// <summary>How many results each query retrieves; recall is measured at this K.</summary>
    public const int GoldK = 10;

    /// <summary>Thirty single-chunk documents across five silos.</summary>
    public static IReadOnlyList<DocumentChunk> BuildChunks()
    {
        var specs = new (string DocId, string Silo, string Dept, string Title, string Text)[]
        {
            // --- HR -----------------------------------------------------------
            ("hr-remote", "hr-policies", "HR", "Remote Work Policy",
                "Remote work is allowed up to three days per week with prior manager approval. " +
                "Employees must remain available during core hours of 10 AM to 3 PM in their local time zone. " +
                "All remote workers must connect through the company VPN to reach internal systems."),
            ("hr-vacation", "hr-policies", "HR", "Annual Vacation Policy",
                "Employees accrue twenty paid vacation days per fiscal year, earned monthly. " +
                "Unused vacation days roll over up to a maximum of ten into the next fiscal year. " +
                "Vacation requests longer than ten consecutive days require director approval."),
            ("hr-sick", "hr-policies", "HR", "Sick Leave Policy",
                "Sick leave is unlimited for employees in good standing. " +
                "Notify your manager within twenty-four hours of taking a sick day. " +
                "Sick leave beyond ten consecutive days requires a medical certificate."),
            ("hr-parental", "hr-policies", "HR", "Parental Leave Policy",
                "Parental leave provides sixteen weeks of fully paid time off for new parents. " +
                "Leave may be taken any time within the first twelve months after birth or adoption. " +
                "Notify HR at least thirty days before your intended leave start date."),
            ("hr-stipend", "hr-policies", "HR", "Home Office Equipment Stipend",
                "The home office equipment stipend is fifteen hundred dollars per year. " +
                "Approved items include monitors, keyboards, ergonomic chairs, and standing desks. " +
                "Submit receipts through the expense portal within thirty days of purchase."),
            ("hr-training", "hr-policies", "HR", "Professional Development Budget",
                "Each employee receives an annual professional development budget of two thousand dollars. " +
                "Eligible expenses include conferences, online courses, and certification exams. " +
                "Unused training budget does not carry over to the following year."),
            ("hr-referral", "hr-policies", "HR", "Employee Referral Bonus",
                "The employee referral bonus is three thousand dollars per successful hire. " +
                "The referred candidate must remain employed for at least ninety days. " +
                "Bonuses are paid in the payroll cycle after the ninety-day milestone."),
            ("hr-conduct", "hr-policies", "HR", "Code of Conduct",
                "The code of conduct prohibits harassment, discrimination, and retaliation. " +
                "Conflicts of interest must be disclosed to your manager in writing. " +
                "Violations may be reported anonymously through the ethics hotline."),

            // --- Engineering --------------------------------------------------
            ("eng-vpn", "technical-docs", "Engineering", "VPN Access Runbook",
                "Connect to the corporate VPN before reaching any internal service or database. " +
                "Install the WireGuard client and import the configuration profile from the IT portal. " +
                "If the tunnel drops, rotate your access key and reconnect within the security console."),
            ("eng-oncall", "technical-docs", "Engineering", "On-Call Escalation Runbook",
                "The on-call engineer acknowledges every page within fifteen minutes. " +
                "Escalate to the secondary responder if the incident is not contained in thirty minutes. " +
                "Post a public status update for any customer-facing outage lasting over five minutes."),
            ("eng-deploy", "technical-docs", "Engineering", "Deployment Pipeline",
                "Deployments run through the blue-green pipeline with automated smoke tests. " +
                "A canary fleet receives five percent of traffic before a full rollout. " +
                "Rollback is a single command that repoints the load balancer to the previous release."),
            ("eng-hnsw", "technical-docs", "Engineering", "Vector Index Tuning ADR",
                "The vector index uses HNSW with ef_search of 128 and M of 16 for recall above ninety percent. " +
                "Scalar quantization trades a small recall loss for a fourfold reduction in memory. " +
                "Raise ef_search when recall regressions appear after a re-index."),
            ("eng-postgres", "technical-docs", "Engineering", "Database Backup Policy",
                "The primary PostgreSQL cluster takes a full base backup nightly and ships WAL segments continuously. " +
                "Point-in-time recovery is tested monthly against a restored standby. " +
                "Retention is thirty-five days for base backups and seven days for WAL archives."),
            ("eng-secrets", "technical-docs", "Engineering", "Secrets Management",
                "All credentials live in the managed key vault and are injected at runtime, never committed to source control. " +
                "Service identities authenticate with short-lived tokens that expire after one hour. " +
                "Rotate any leaked secret immediately and audit its access history."),
            ("eng-logging", "technical-docs", "Engineering", "Structured Logging Standard",
                "Every service emits structured JSON logs with a correlation id on each request. " +
                "Logs ship to the central observability platform with a thirty-day hot retention window. " +
                "Never log secrets, access tokens, or personally identifiable information."),

            // --- Finance ------------------------------------------------------
            ("fin-expenses", "financial-reports", "Finance", "Travel Expense Reimbursement",
                "Business travel expenses are reimbursed when submitted with itemised receipts. " +
                "Daily meal allowances are capped at seventy-five dollars while travelling. " +
                "Airfare must be booked in economy class unless a flight exceeds six hours."),
            ("fin-invoicing", "financial-reports", "Finance", "Vendor Invoicing in FreshBooks",
                "Vendor invoices are entered into FreshBooks within two business days of receipt. " +
                "Net-thirty terms apply unless the contract specifies otherwise. " +
                "Payments above ten thousand dollars require a second approver."),
            ("fin-budget", "financial-reports", "Finance", "Quarterly Budget Review",
                "Department heads review their budget variance at the close of every quarter. " +
                "Overruns above five percent require a written justification to Finance. " +
                "Reallocations between cost centres need CFO sign-off."),
            ("fin-payroll", "financial-reports", "Finance", "Payroll Schedule",
                "Salaries are paid on the last business day of each month by direct deposit. " +
                "Timesheet corrections must be submitted three business days before the payroll run. " +
                "Year-end tax documents are issued by the end of January."),
            ("fin-procurement", "financial-reports", "Finance", "Procurement Policy",
                "Purchases above five thousand dollars require three competitive quotes. " +
                "A signed purchase order must precede any committed spend. " +
                "Preferred suppliers are listed in the procurement catalogue."),
            ("fin-revenue", "financial-reports", "Finance", "Revenue Recognition",
                "Subscription revenue is recognised rateably over the contract term. " +
                "Setup fees are deferred and amortised across the first twelve months. " +
                "Refunds are netted against revenue in the period they are issued."),

            // --- Legal --------------------------------------------------------
            ("legal-nda", "legal-contracts", "Legal", "Non-Disclosure Agreement Terms",
                "The mutual non-disclosure agreement binds both parties for a term of five years. " +
                "Confidential information excludes anything already public or independently developed. " +
                "Breach entitles the disclosing party to injunctive relief."),
            ("legal-dpa", "legal-contracts", "Legal", "Data Processing Addendum",
                "The data processing addendum governs how customer personal data is handled under GDPR. " +
                "Sub-processors are listed in an annex and customers are notified before any change. " +
                "Data is deleted or returned within ninety days of contract termination."),
            ("legal-sla", "legal-contracts", "Legal", "Service Level Agreement",
                "The service level agreement guarantees ninety-nine point nine percent monthly uptime. " +
                "Service credits apply when availability falls below the committed threshold. " +
                "Scheduled maintenance windows are excluded from the uptime calculation."),
            ("legal-ip", "legal-contracts", "Legal", "Intellectual Property Assignment",
                "Work product created during employment is assigned to the company. " +
                "Prior inventions listed in the onboarding schedule remain the employee's property. " +
                "Open-source contributions require prior written approval."),
            ("legal-retention", "legal-contracts", "Legal", "Records Retention Schedule",
                "Financial records are retained for seven years to satisfy audit requirements. " +
                "Employment records are kept for the duration of employment plus three years. " +
                "Expired records are destroyed through the certified shredding vendor."),

            // --- Product ------------------------------------------------------
            ("prod-roadmap", "product-catalog", "Product", "Roadmap Prioritisation",
                "Roadmap items are scored on reach, impact, confidence, and effort. " +
                "The top quartile is committed for the quarter and the rest is backlog. " +
                "Customer-commitment features can jump the queue with VP approval."),
            ("prod-pricing", "product-catalog", "Product", "Pricing Tiers",
                "The product ships in Starter, Team, and Enterprise tiers billed annually. " +
                "Enterprise adds single sign-on, audit logs, and a dedicated success manager. " +
                "Usage above the plan limit is billed as metered overage."),
            ("prod-flags", "product-catalog", "Product", "Feature Flag Rollout",
                "New features ship behind a feature flag and are enabled for internal users first. " +
                "A staged rollout expands to one, ten, and fifty percent of accounts over a week. " +
                "Any flag can be killed instantly from the experimentation dashboard."),
            ("prod-feedback", "product-catalog", "Product", "Customer Feedback Loop",
                "Feedback from support tickets and interviews is tagged and clustered weekly. " +
                "Themes with the highest weighted demand feed the next planning cycle. " +
                "Every shipped feature links back to the requests that motivated it."),
        };

        var chunks = new List<DocumentChunk>(specs.Length);
        foreach (var (docId, silo, dept, title, text) in specs)
        {
            var meta = new DocumentMetadata(
                Id: docId,
                Silo: silo,
                Department: dept,
                Office: "Montreal",
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
    /// Fifty gold (query → expected document) pairs. Each query is phrased in the
    /// user's words; the expected document is the one whose chunk answers it.
    /// </summary>
    public static IReadOnlyList<GoldenItem> BuildGoldQueries()
    {
        (string Query, string ExpectedDocId)[] pairs =
        [
            // HR (12)
            ("How many days a week can I work from home?",                  "hr-remote"),
            ("What are the core hours I need to be online?",                "hr-remote"),
            ("How many paid vacation days do I get each year?",             "hr-vacation"),
            ("Can unused vacation roll over to next year?",                 "hr-vacation"),
            ("What do I do when I am too sick to work?",                    "hr-sick"),
            ("When do I need a doctor's note for being out?",               "hr-sick"),
            ("How much time off do new parents get?",                       "hr-parental"),
            ("What is the budget for a desk and chair at home?",            "hr-stipend"),
            ("Can the company pay for a certification exam?",               "hr-training"),
            ("What is the bonus for referring someone we hire?",            "hr-referral"),
            ("Where do I report harassment anonymously?",                   "hr-conduct"),
            ("Do I have to disclose a conflict of interest?",               "hr-conduct"),

            // Engineering (10)
            ("How do I connect to the VPN?",                                "eng-vpn"),
            ("Which VPN client should I install?",                          "eng-vpn"),
            ("How fast must on-call acknowledge a page?",                   "eng-oncall"),
            ("When do I escalate an incident to the secondary?",            "eng-oncall"),
            ("How do we roll back a bad release?",                          "eng-deploy"),
            ("What ef_search and M values does the HNSW index use?",        "eng-hnsw"),
            ("How does scalar quantization affect recall?",                 "eng-hnsw"),
            ("How long are PostgreSQL backups retained?",                   "eng-postgres"),
            ("Where are application secrets stored?",                       "eng-secrets"),
            ("What must never appear in our logs?",                         "eng-logging"),

            // Finance (10)
            ("What is the daily meal limit when I travel for work?",        "fin-expenses"),
            ("When must airfare be booked in economy?",                     "fin-expenses"),
            ("How quickly are vendor invoices entered in FreshBooks?",      "fin-invoicing"),
            ("Who approves a large vendor payment?",                        "fin-invoicing"),
            ("Who signs off on moving money between cost centres?",         "fin-budget"),
            ("When are salaries paid each month?",                          "fin-payroll"),
            ("How many quotes are needed for a big purchase?",              "fin-procurement"),
            ("How is subscription revenue recognised?",                     "fin-revenue"),
            ("Are setup fees recognised immediately?",                      "fin-revenue"),
            ("When are year-end tax documents issued?",                     "fin-payroll"),

            // Legal (10)
            ("How long does the NDA stay in effect?",                       "legal-nda"),
            ("What information is not covered by the NDA?",                  "legal-nda"),
            ("How is customer personal data handled under GDPR?",           "legal-dpa"),
            ("When is data deleted after a contract ends?",                 "legal-dpa"),
            ("What uptime does the SLA promise?",                           "legal-sla"),
            ("When do service credits apply?",                              "legal-sla"),
            ("Who owns code I write while employed here?",                  "legal-ip"),
            ("Do I need approval for open-source contributions?",           "legal-ip"),
            ("How long do we keep financial records?",                      "legal-retention"),
            ("How are expired records destroyed?",                          "legal-retention"),

            // Product (8)
            ("How are roadmap items prioritised?",                          "prod-roadmap"),
            ("What is included in the Enterprise tier?",                    "prod-pricing"),
            ("How is usage over the plan limit billed?",                    "prod-pricing"),
            ("How do we stage a feature flag rollout?",                     "prod-flags"),
            ("Can we instantly disable a new feature?",                     "prod-flags"),
            ("How is customer feedback turned into roadmap input?",         "prod-feedback"),
            ("What pricing tiers does the product offer?",                  "prod-pricing"),
            ("How does a feature reach fifty percent of accounts?",         "prod-flags"),
        ];

        return pairs
            .Select(p => new GoldenItem(
                Query: p.Query,
                ExpectedDocumentIds: new HashSet<string>(StringComparer.Ordinal) { p.ExpectedDocId }))
            .ToList();
    }
}
