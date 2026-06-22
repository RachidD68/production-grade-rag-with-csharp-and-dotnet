using SmartDocs.Core.Documents;

namespace RagInDotNet.Samples.Ch19_MultiAgentOrchestration;

/// <summary>
/// A deterministic ~150-chunk SmartDocs corpus spanning five silos. Each topic
/// expands into several single-sentence chunks so the offline retriever has a
/// realistic candidate pool to rank over — large enough to make the multi-agent
/// graph do real work, small enough to stay instant and key-free.
/// </summary>
internal static class Corpus
{
    public static IReadOnlyList<DocumentChunk> Build()
    {
        var topics = new (string Silo, string Dept, string Title, string[] Facts)[]
        {
            ("hr-policies", "HR", "Remote Work Policy", new[]
            {
                "Remote work is allowed up to three days per week with prior manager approval.",
                "Remote employees must stay reachable during core hours of 10 AM to 3 PM local time.",
                "All remote workers connect through the company VPN to reach internal systems.",
                "A fully remote arrangement requires a signed remote-work agreement and director sign-off.",
            }),
            ("hr-policies", "HR", "Vacation Policy", new[]
            {
                "Employees accrue twenty paid vacation days per fiscal year, earned monthly.",
                "Unused vacation rolls over up to a maximum of ten days into the next year.",
                "Vacation requests longer than ten consecutive days require director approval.",
                "Vacation balances are visible in the HR portal and update on the first of each month.",
            }),
            ("hr-policies", "HR", "Parental Leave Policy", new[]
            {
                "Parental leave provides sixteen weeks of fully paid time off for new parents.",
                "Leave may be taken any time within twelve months after birth or adoption.",
                "Notify HR at least thirty days before the intended leave start date.",
                "Parental leave can be split into two blocks with manager agreement.",
            }),
            ("hr-policies", "HR", "Sick Leave Policy", new[]
            {
                "Sick leave is unlimited for employees in good standing.",
                "Notify your manager within twenty-four hours of taking a sick day.",
                "Sick leave beyond ten consecutive days requires a medical certificate.",
                "Mental-health days count as sick leave and need no separate justification.",
            }),
            ("hr-policies", "HR", "Equipment Stipend", new[]
            {
                "The home office equipment stipend is fifteen hundred dollars per year.",
                "Approved items include monitors, keyboards, ergonomic chairs, and standing desks.",
                "Submit receipts through the expense portal within thirty days of purchase.",
                "The stipend resets each fiscal year and does not roll over.",
            }),
            ("hr-policies", "HR", "Professional Development", new[]
            {
                "Each employee receives an annual development budget of two thousand dollars.",
                "Eligible expenses include conferences, online courses, and certification exams.",
                "Unused training budget does not carry over to the following year.",
                "Manager approval is required before booking any conference travel.",
            }),

            ("technical-docs", "Engineering", "VPN Access Runbook", new[]
            {
                "Connect to the corporate VPN before reaching any internal service or database.",
                "Install the WireGuard client and import the profile from the IT portal.",
                "If the tunnel drops, rotate your access key and reconnect in the security console.",
                "VPN access is granted per team and reviewed every quarter.",
            }),
            ("technical-docs", "Engineering", "On-Call Escalation", new[]
            {
                "The on-call engineer acknowledges every page within fifteen minutes.",
                "Escalate to the secondary responder if not contained within thirty minutes.",
                "Post a public status update for any customer-facing outage over five minutes.",
                "Every incident gets a blameless postmortem within five business days.",
            }),
            ("technical-docs", "Engineering", "Deployment Pipeline", new[]
            {
                "Deployments run through the blue-green pipeline with automated smoke tests.",
                "A canary fleet receives five percent of traffic before a full rollout.",
                "Rollback is a single command that repoints the load balancer to the prior release.",
                "Database migrations run in a separate, reversible step before the app deploys.",
            }),
            ("technical-docs", "Engineering", "Vector Index Tuning", new[]
            {
                "The vector index uses HNSW with ef_search of 128 and M of 16 for recall above ninety percent.",
                "Scalar quantization trades a small recall loss for a fourfold memory reduction.",
                "Raise ef_search when recall regressions appear after a re-index.",
                "Index builds run nightly and are validated against a held-out recall set.",
            }),
            ("technical-docs", "Engineering", "Secrets Management", new[]
            {
                "All credentials live in the managed key vault and are injected at runtime.",
                "Service identities authenticate with short-lived tokens that expire after one hour.",
                "Rotate any leaked secret immediately and audit its access history.",
                "Secrets are never committed to source control or written to logs.",
            }),
            ("technical-docs", "Engineering", "Structured Logging", new[]
            {
                "Every service emits structured JSON logs with a correlation id per request.",
                "Logs ship to the central platform with a thirty-day hot retention window.",
                "Never log secrets, access tokens, or personally identifiable information.",
                "Log levels are configurable per service without a redeploy.",
            }),

            ("financial-reports", "Finance", "Travel Expenses", new[]
            {
                "Business travel is reimbursed when submitted with itemised receipts.",
                "Daily meal allowances are capped at seventy-five dollars while travelling.",
                "Airfare must be booked in economy unless a flight exceeds six hours.",
                "Expense reports are due within fifteen days of returning from a trip.",
            }),
            ("financial-reports", "Finance", "Vendor Invoicing", new[]
            {
                "Vendor invoices are entered into the ledger within two business days of receipt.",
                "Net-thirty terms apply unless the contract specifies otherwise.",
                "Payments above ten thousand dollars require a second approver.",
                "Duplicate invoices are flagged automatically before payment runs.",
            }),
            ("financial-reports", "Finance", "Budget Review", new[]
            {
                "Department heads review budget variance at the close of every quarter.",
                "Overruns above five percent require a written justification to Finance.",
                "Reallocations between cost centres need CFO sign-off.",
                "Quarterly forecasts feed the next year's planning cycle.",
            }),
            ("financial-reports", "Finance", "Payroll Schedule", new[]
            {
                "Salaries are paid on the last business day of each month by direct deposit.",
                "Timesheet corrections are due three business days before the payroll run.",
                "Year-end tax documents are issued by the end of January.",
                "Payroll changes take effect the cycle after they are approved.",
            }),
            ("financial-reports", "Finance", "Revenue Recognition", new[]
            {
                "Subscription revenue is recognised rateably over the contract term.",
                "Setup fees are deferred and amortised across the first twelve months.",
                "Refunds are netted against revenue in the period they are issued.",
                "Multi-year contracts are split into annual recognition schedules.",
            }),

            ("legal-contracts", "Legal", "Non-Disclosure Agreement", new[]
            {
                "The mutual non-disclosure agreement binds both parties for five years.",
                "Confidential information excludes anything already public or independently developed.",
                "Breach entitles the disclosing party to injunctive relief.",
                "The NDA survives termination of the underlying commercial agreement.",
            }),
            ("legal-contracts", "Legal", "Data Processing Addendum", new[]
            {
                "The data processing addendum governs how customer personal data is handled under GDPR.",
                "Sub-processors are listed in an annex and customers are notified before any change.",
                "Data is deleted or returned within ninety days of contract termination.",
                "The addendum requires breach notification within seventy-two hours.",
            }),
            ("legal-contracts", "Legal", "Service Level Agreement", new[]
            {
                "The service level agreement guarantees ninety-nine point nine percent monthly uptime.",
                "Service credits apply when availability falls below the committed threshold.",
                "Scheduled maintenance windows are excluded from the uptime calculation.",
                "Uptime is measured at the load balancer over a calendar month.",
            }),
            ("legal-contracts", "Legal", "Intellectual Property", new[]
            {
                "Work product created during employment is assigned to the company.",
                "Prior inventions listed in the onboarding schedule remain the employee's property.",
                "Open-source contributions require prior written approval.",
                "Patentable inventions are disclosed to Legal before any public mention.",
            }),
            ("legal-contracts", "Legal", "Records Retention", new[]
            {
                "Financial records are retained for seven years to satisfy audit requirements.",
                "Employment records are kept for the duration of employment plus three years.",
                "Expired records are destroyed through the certified shredding vendor.",
                "Legal holds suspend destruction for any records under litigation.",
            }),

            ("product-catalog", "Product", "Roadmap Prioritisation", new[]
            {
                "Roadmap items are scored on reach, impact, confidence, and effort.",
                "The top quartile is committed for the quarter and the rest is backlog.",
                "Customer-commitment features can jump the queue with VP approval.",
                "Every committed item has a named directly-responsible individual.",
            }),
            ("product-catalog", "Product", "Pricing Tiers", new[]
            {
                "The product ships in Starter, Team, and Enterprise tiers billed annually.",
                "Enterprise adds single sign-on, audit logs, and a dedicated success manager.",
                "Usage above the plan limit is billed as metered overage.",
                "Annual plans receive a discount over the monthly equivalent.",
            }),
            ("product-catalog", "Product", "Feature Flag Rollout", new[]
            {
                "New features ship behind a feature flag and reach internal users first.",
                "A staged rollout expands to one, ten, and fifty percent of accounts over a week.",
                "Any flag can be killed instantly from the experimentation dashboard.",
                "Flag state is audited so every change is attributable to a person.",
            }),
            ("product-catalog", "Product", "Customer Feedback", new[]
            {
                "Feedback from tickets and interviews is tagged and clustered weekly.",
                "Themes with the highest weighted demand feed the next planning cycle.",
                "Every shipped feature links back to the requests that motivated it.",
                "Net promoter survey results are reviewed by the product leadership monthly.",
            }),
            ("product-catalog", "Product", "Support Tiers", new[]
            {
                "Standard support answers within one business day during local working hours.",
                "Priority support answers within four hours, around the clock.",
                "Enterprise customers get a named technical account manager.",
                "Severity-one incidents page the on-call support lead immediately.",
            }),

            ("hr-policies", "HR", "Referral Bonus", new[]
            {
                "The employee referral bonus is three thousand dollars per successful hire.",
                "The referred candidate must remain employed for at least ninety days.",
                "Bonuses are paid in the payroll cycle after the ninety-day milestone.",
                "There is no limit on the number of referral bonuses an employee can earn.",
            }),
            ("hr-policies", "HR", "Code of Conduct", new[]
            {
                "The code of conduct prohibits harassment, discrimination, and retaliation.",
                "Conflicts of interest must be disclosed to your manager in writing.",
                "Violations may be reported anonymously through the ethics hotline.",
                "All employees complete code-of-conduct training every year.",
            }),
            ("technical-docs", "Engineering", "Database Backup", new[]
            {
                "The primary PostgreSQL cluster takes a full base backup nightly and ships WAL continuously.",
                "Point-in-time recovery is tested monthly against a restored standby.",
                "Retention is thirty-five days for base backups and seven days for WAL archives.",
                "Restore drills are timed and the runbook is updated after each one.",
            }),
            ("technical-docs", "Engineering", "API Versioning", new[]
            {
                "Public APIs are versioned in the URL path and supported for eighteen months.",
                "Breaking changes ship only in a new major version, never in a patch.",
                "Deprecations are announced at least ninety days before removal.",
                "Each version has its own OpenAPI document published to the developer portal.",
            }),
            ("financial-reports", "Finance", "Procurement Policy", new[]
            {
                "Purchases above five thousand dollars require three competitive quotes.",
                "A signed purchase order must precede any committed spend.",
                "Preferred suppliers are listed in the procurement catalogue.",
                "Sole-source purchases need a written justification and director approval.",
            }),
            ("financial-reports", "Finance", "Corporate Cards", new[]
            {
                "Corporate cards are issued to managers and frequent travellers on request.",
                "Card statements are reconciled against receipts within ten days of the cycle close.",
                "Personal charges on a corporate card must be repaid the same month.",
                "Lost or stolen cards are reported to Finance and the issuer immediately.",
            }),
            ("legal-contracts", "Legal", "Acceptable Use", new[]
            {
                "The acceptable use policy prohibits using the service for unlawful content.",
                "Automated scraping beyond published rate limits is not permitted.",
                "The company may suspend accounts that threaten platform stability.",
                "Reported abuse is reviewed within one business day.",
            }),
            ("legal-contracts", "Legal", "Warranty Terms", new[]
            {
                "The software is warranted to perform materially as documented for ninety days.",
                "The sole remedy for a breach of warranty is repair or replacement.",
                "The warranty excludes defects caused by misuse or unauthorised modification.",
                "Warranty claims are submitted in writing to the support address.",
            }),
            ("product-catalog", "Product", "Onboarding Flow", new[]
            {
                "New accounts complete a guided setup that imports a starter workspace.",
                "An interactive checklist tracks the first five high-value actions.",
                "Customers who finish onboarding in week one retain at a markedly higher rate.",
                "Onboarding emails are paused as soon as the matching action is completed.",
            }),
            ("product-catalog", "Product", "Integrations Catalogue", new[]
            {
                "The integrations catalogue lists every supported third-party connector.",
                "Each connector documents its scopes, rate limits, and data flow.",
                "Connectors are certified before they appear in the public catalogue.",
                "Customers can request a new connector through the feedback portal.",
            }),
            ("technical-docs", "Engineering", "Incident Severity", new[]
            {
                "Severity one is a full outage or data loss affecting many customers.",
                "Severity two is a major degradation with a viable workaround.",
                "Severity three is a minor issue with limited customer impact.",
                "Severity is reassessed as new information arrives during an incident.",
            }),
        };

        var chunks = new List<DocumentChunk>();
        var docCounter = 0;
        foreach (var (silo, dept, title, facts) in topics)
        {
            docCounter++;
            var docId = $"{silo}-{docCounter:D2}";
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

            for (var i = 0; i < facts.Length; i++)
            {
                var text = $"{title}: {facts[i]}";
                chunks.Add(new DocumentChunk(
                    ChunkId: $"{docId}#{i}",
                    DocumentId: docId,
                    ChunkIndex: i,
                    Text: text,
                    StartCharOffset: 0,
                    EndCharOffset: text.Length,
                    Metadata: meta));
            }
        }

        return chunks;
    }
}
