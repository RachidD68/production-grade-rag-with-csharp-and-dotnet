using SmartDocs.Core.Documents;

namespace RagInDotNet.Samples.Ch18_McpServer;

/// <summary>
/// A deterministic ~180-chunk SmartDocs HR / policy corpus for the MCP server.
/// Built from a fixed set of policy documents, each split into several short
/// chunks, across five silos and the full range of confidentiality levels
/// (Public → Confidential) so the boundary tenant scope has something to
/// enforce. No model or API key required — the FNV bag-of-words embedder turns
/// this into reproducible vectors.
/// </summary>
internal static class Corpus
{
    /// <summary>Build the corpus chunks. Stable order, stable ids.</summary>
    public static IReadOnlyList<DocumentChunk> Build()
    {
        // (docId, silo, dept, office, confidentiality, title, paragraphs[])
        var specs = new (string DocId, string Silo, string Dept, string Office, string Level, string Title, string[] Paras)[]
        {
            ("hr-remote", "hr-policies", "HR", "Montreal", "Public", "Remote Work Policy",
            [
                "Remote work is allowed up to three days per week with prior manager approval.",
                "Employees must remain available during core hours of 10 AM to 3 PM in their local time zone.",
                "All remote workers connect through the company VPN to reach internal systems.",
                "A signed remote-work agreement is required before the first remote day.",
            ]),
            ("hr-vacation", "hr-policies", "HR", "Montreal", "Public", "Annual Vacation Policy",
            [
                "Employees accrue twenty paid vacation days per fiscal year, earned monthly.",
                "Unused vacation days roll over up to a maximum of ten into the next fiscal year.",
                "Vacation requests longer than ten consecutive days require director approval.",
                "Vacation balances are visible in the self-service HR portal.",
            ]),
            ("hr-sick", "hr-policies", "HR", "Montreal", "Internal", "Sick Leave Policy",
            [
                "Sick leave is unlimited for employees in good standing.",
                "Notify your manager within twenty-four hours of taking a sick day.",
                "Sick leave beyond ten consecutive days requires a medical certificate.",
                "Extended medical leave is coordinated with the benefits team.",
            ]),
            ("hr-parental", "hr-policies", "HR", "Paris", "Internal", "Parental Leave Policy",
            [
                "Parental leave provides sixteen weeks of fully paid time off for new parents.",
                "Leave may be taken any time within the first twelve months after birth or adoption.",
                "Notify HR at least thirty days before your intended leave start date.",
                "Parental leave can be split into two blocks with manager agreement.",
            ]),
            ("hr-stipend", "hr-policies", "HR", "Casablanca", "Internal", "Home Office Equipment Stipend",
            [
                "The home office equipment stipend is fifteen hundred dollars per year.",
                "Approved items include monitors, keyboards, ergonomic chairs, and standing desks.",
                "Submit receipts through the expense portal within thirty days of purchase.",
            ]),
            ("hr-comp", "hr-policies", "HR", "Montreal", "Confidential", "Compensation Bands",
            [
                "Compensation bands are confidential and define the salary range for each level.",
                "Band placement reflects scope, impact, and market benchmarks reviewed annually.",
                "Managers may discuss an employee's own band but never another employee's.",
                "Out-of-band offers require VP of People approval and a written justification.",
            ]),
            ("hr-conduct", "hr-policies", "HR", "Montreal", "Public", "Code of Conduct",
            [
                "The code of conduct prohibits harassment, discrimination, and retaliation.",
                "Conflicts of interest must be disclosed to your manager in writing.",
                "Violations may be reported anonymously through the ethics hotline.",
            ]),
            ("eng-vpn", "technical-docs", "Engineering", "Montreal", "Internal", "VPN Access Runbook",
            [
                "Connect to the corporate VPN before reaching any internal service or database.",
                "Install the WireGuard client and import the configuration profile from the IT portal.",
                "If the tunnel drops, rotate your access key and reconnect within the security console.",
                "Report a lost device to security immediately so its key can be revoked.",
            ]),
            ("eng-oncall", "technical-docs", "Engineering", "Montreal", "Internal", "On-Call Escalation Runbook",
            [
                "The on-call engineer acknowledges every page within fifteen minutes.",
                "Escalate to the secondary responder if the incident is not contained in thirty minutes.",
                "Post a public status update for any customer-facing outage lasting over five minutes.",
                "Write a blameless postmortem within three business days of resolution.",
            ]),
            ("eng-deploy", "technical-docs", "Engineering", "Paris", "Internal", "Deployment Pipeline",
            [
                "Deployments run through the blue-green pipeline with automated smoke tests.",
                "A canary fleet receives five percent of traffic before a full rollout.",
                "Rollback is a single command that repoints the load balancer to the previous release.",
            ]),
            ("eng-hnsw", "technical-docs", "Engineering", "Montreal", "Internal", "Vector Index Tuning ADR",
            [
                "The vector index uses HNSW with ef_search of 128 and M of 16 for recall above ninety percent.",
                "Scalar quantization trades a small recall loss for a fourfold reduction in memory.",
                "Raise ef_search when recall regressions appear after a re-index.",
            ]),
            ("eng-secrets", "technical-docs", "Engineering", "Montreal", "Restricted", "Secrets Management",
            [
                "All credentials live in the managed key vault and are injected at runtime, never committed to source control.",
                "Service identities authenticate with short-lived tokens that expire after one hour.",
                "Rotate any leaked secret immediately and audit its access history.",
                "Production vault access is restricted to the on-call rotation.",
            ]),
            ("eng-incident", "technical-docs", "Engineering", "Casablanca", "Restricted", "Security Incident Response",
            [
                "A suspected breach is declared a security incident and escalated to the security lead.",
                "Contain the blast radius first, then preserve forensic evidence before remediation.",
                "Customer notification follows the legal team's guidance and regulatory deadlines.",
            ]),
            ("fin-expenses", "financial-reports", "Finance", "Montreal", "Internal", "Travel Expense Reimbursement",
            [
                "Business travel expenses are reimbursed when submitted with itemised receipts.",
                "Daily meal allowances are capped at seventy-five dollars while travelling.",
                "Airfare must be booked in economy class unless a flight exceeds six hours.",
            ]),
            ("fin-invoicing", "financial-reports", "Finance", "Paris", "Internal", "Vendor Invoicing",
            [
                "Vendor invoices are entered into the finance system within two business days of receipt.",
                "Net-thirty terms apply unless the contract specifies otherwise.",
                "Payments above ten thousand dollars require a second approver.",
            ]),
            ("fin-budget", "financial-reports", "Finance", "Montreal", "Confidential", "Quarterly Budget Review",
            [
                "Department budgets and variance are confidential and reviewed at the close of every quarter.",
                "Overruns above five percent require a written justification to Finance.",
                "Reallocations between cost centres need CFO sign-off.",
            ]),
            ("fin-payroll", "financial-reports", "Finance", "Casablanca", "Confidential", "Payroll Schedule",
            [
                "Salaries are paid on the last business day of each month by direct deposit.",
                "Timesheet corrections must be submitted three business days before the payroll run.",
                "Individual payroll records are confidential and accessible only to Finance and the employee.",
            ]),
            ("legal-nda", "legal-contracts", "Legal", "Montreal", "Internal", "Non-Disclosure Agreement Terms",
            [
                "The mutual non-disclosure agreement binds both parties for a term of five years.",
                "Confidential information excludes anything already public or independently developed.",
                "Breach entitles the disclosing party to injunctive relief.",
            ]),
            ("legal-dpa", "legal-contracts", "Legal", "Paris", "Restricted", "Data Processing Addendum",
            [
                "The data processing addendum governs how customer personal data is handled under GDPR.",
                "Sub-processors are listed in an annex and customers are notified before any change.",
                "Data is deleted or returned within ninety days of contract termination.",
            ]),
            ("legal-sla", "legal-contracts", "Legal", "Montreal", "Public", "Service Level Agreement",
            [
                "The service level agreement guarantees ninety-nine point nine percent monthly uptime.",
                "Service credits apply when availability falls below the committed threshold.",
                "Scheduled maintenance windows are excluded from the uptime calculation.",
            ]),
            ("legal-ip", "legal-contracts", "Legal", "Montreal", "Internal", "Intellectual Property Assignment",
            [
                "Work product created during employment is assigned to the company.",
                "Prior inventions listed in the onboarding schedule remain the employee's property.",
                "Open-source contributions require prior written approval.",
            ]),
            ("prod-roadmap", "product-catalog", "Product", "Montreal", "Internal", "Roadmap Prioritisation",
            [
                "Roadmap items are scored on reach, impact, confidence, and effort.",
                "The top quartile is committed for the quarter and the rest is backlog.",
                "Customer-commitment features can jump the queue with VP approval.",
            ]),
            ("prod-pricing", "product-catalog", "Product", "Paris", "Public", "Pricing Tiers",
            [
                "The product ships in Starter, Team, and Enterprise tiers billed annually.",
                "Enterprise adds single sign-on, audit logs, and a dedicated success manager.",
                "Usage above the plan limit is billed as metered overage.",
            ]),
            ("prod-flags", "product-catalog", "Product", "Casablanca", "Internal", "Feature Flag Rollout",
            [
                "New features ship behind a feature flag and are enabled for internal users first.",
                "A staged rollout expands to one, ten, and fifty percent of accounts over a week.",
                "Any flag can be killed instantly from the experimentation dashboard.",
            ]),
        };

        var chunks = new List<DocumentChunk>();
        foreach (var spec in specs)
        {
            var meta = new DocumentMetadata(
                Id: spec.DocId,
                Silo: spec.Silo,
                Department: spec.Dept,
                Office: spec.Office,
                ConfidentialityLevel: spec.Level,
                DocumentType: "Policy",
                FiscalYear: 2026,
                Author: "SmartDocs",
                LastModified: new DateOnly(2026, 1, 1),
                Title: spec.Title);

            for (var i = 0; i < spec.Paras.Length; i++)
            {
                var text = spec.Paras[i];
                chunks.Add(new DocumentChunk(
                    ChunkId: $"{spec.DocId}#{i}",
                    DocumentId: spec.DocId,
                    ChunkIndex: i,
                    Text: text,
                    StartCharOffset: 0,
                    EndCharOffset: text.Length,
                    Metadata: meta));
            }
        }

        // Synthesise additional procedural chunks so the corpus lands around the
        // ~150–200-chunk target the chapter calls for, without inventing new
        // policies: each base document gets a few short "see also" / FAQ chunks.
        var baseCount = chunks.Count;
        var faqTemplates = new[]
        {
            "Frequently asked: who approves an exception to the {0} policy? Your manager, then the policy owner.",
            "Related procedure: how to request a change to the {0} policy through the HR portal.",
            "Quick reference: the {0} policy was last reviewed in fiscal year 2026 and applies company-wide.",
            "See also: escalation contacts and the service-desk queue for {0} questions.",
        };
        foreach (var spec in specs)
        {
            var meta = new DocumentMetadata(
                spec.DocId, spec.Silo, spec.Dept, spec.Office, spec.Level, "Reference",
                2026, "SmartDocs", new DateOnly(2026, 1, 1), spec.Title + " — Reference");
            for (var i = 0; i < faqTemplates.Length; i++)
            {
                var text = string.Format(System.Globalization.CultureInfo.InvariantCulture, faqTemplates[i], spec.Title);
                var idx = 100 + i; // keep the chunk-index space separate from the body chunks
                chunks.Add(new DocumentChunk(
                    ChunkId: $"{spec.DocId}#{idx}",
                    DocumentId: spec.DocId,
                    ChunkIndex: idx,
                    Text: text,
                    StartCharOffset: 0,
                    EndCharOffset: text.Length,
                    Metadata: meta));
            }
        }

        _ = baseCount;
        return chunks;
    }
}
