using System.Globalization;
using System.Text;

namespace RagInDotNet.Tools.GenerateDataset;

/// <summary>Per-silo synthetic content generators. Templated phrase banks; no LLM calls.</summary>
internal static class SiloContent
{
    private static readonly string[] Offices = { "Montreal", "Paris", "Casablanca" };
    private static readonly string[] Authors =
    {
        "Amélie Tremblay", "Jean-Baptiste Roy", "Sofia Benali", "Hugo Laurent",
        "Yasmine El Khalidi", "Marc Dubois", "Léa Martin", "Karim Idrissi",
        "Charlotte Bélanger", "Omar Belhaj",
    };
    private static readonly string[] FiscalYears = { "2024", "2025", "2026" };

    // ---------------------------------------------------------------------
    // HR Policies
    // ---------------------------------------------------------------------
    private static readonly string[] HrTopics =
    {
        "Annual Leave", "Sick Leave", "Parental Leave", "Bereavement Leave",
        "Code of Conduct", "Anti-Harassment", "Remote Work", "Travel Reimbursement",
        "Salary Review Cycle", "Performance Improvement Plan", "Probation Terms",
        "Stock Option Plan", "Health Benefits", "Pension Contributions",
        "Diversity & Inclusion", "Office Hours and Flex-Time", "Onboarding Checklist",
        "Offboarding Process", "Internal Mobility", "Whistleblower Policy",
    };

    public static SyntheticDocument HrPolicy(Random rng, int index)
    {
        var topic = HrTopics[rng.Next(HrTopics.Length)];
        var office = Offices[rng.Next(Offices.Length)];
        var year = FiscalYears[rng.Next(FiscalYears.Length)];

        var title = $"{topic} Policy ({office}, FY{year})";

        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"## Purpose").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture,
            $"This policy describes Contoso Intelligent Systems' approach to **{topic}** for employees based in the {office} office during fiscal year {year}.").AppendLine();

        body.AppendLine("## Scope").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture,
            $"Applies to all permanent and contract employees of the {office} office. Interns and external consultants are covered only where the agreement explicitly references this policy.").AppendLine();

        body.AppendLine("## Eligibility").AppendLine();
        var probationDays = 30 + (rng.Next(0, 4) * 30);
        var entitlementDays = 15 + rng.Next(0, 16);
        body.AppendLine(CultureInfo.InvariantCulture,
            $"Employees become eligible after a {probationDays}-day probation period. Eligible employees receive {entitlementDays} working days per fiscal year, accrued monthly.").AppendLine();

        body.AppendLine("## Procedure").AppendLine();
        body.AppendLine("1. Submit a request through the People Hub portal at least two weeks before the intended date.");
        body.AppendLine("2. The line manager confirms or declines within five business days.");
        body.AppendLine("3. If declined, the employee may escalate to the HR Business Partner for the office.");
        body.AppendLine();

        body.AppendLine("## Approvals").AppendLine();
        body.AppendLine($"- Line manager: required for all requests.");
        body.AppendLine($"- HR Business Partner ({office}): required for requests longer than 10 working days.");
        body.AppendLine($"- Office Director ({office}): required for any extension beyond the standard entitlement.");
        body.AppendLine();

        body.AppendLine("## Related Policies").AppendLine();
        body.AppendLine("See the Employee Handbook, Section 3 (Time Away from Work) for cross-references and exceptions.");

        return new SyntheticDocument(
            Id: $"hr-{index:D3}",
            Silo: "hr-policies",
            Department: "HR",
            Office: office,
            ConfidentialityLevel: "Internal",
            DocumentType: "Policy",
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: RandomDate(rng, year),
            Title: title,
            Body: body.ToString());
    }

    // ---------------------------------------------------------------------
    // Technical Docs (ADRs, runbooks, API references)
    // ---------------------------------------------------------------------
    private static readonly string[] TechSubjects =
    {
        "User Authentication Service", "Billing Service", "Notification Pipeline",
        "Search Index Builder", "PDF Renderer", "Audit Log Aggregator",
        "Feature Flag Service", "Metrics Collector", "Webhook Dispatcher",
        "Tenant Provisioning Worker", "Document Ingestion Queue", "Reporting API",
    };
    private static readonly string[] AdrDecisions =
    {
        "Adopt PostgreSQL for transactional data", "Standardize on OpenTelemetry for tracing",
        "Migrate Redis cache from cluster to managed service", "Move queue layer from RabbitMQ to Azure Service Bus",
        "Replace synchronous webhooks with idempotent retries", "Adopt feature-flag service for progressive rollout",
        "Use ASP.NET Core Minimal APIs over MVC for new services", "Standardize on JSON-LD for export formats",
    };

    public static SyntheticDocument TechnicalDoc(Random rng, int index)
    {
        var docType = (index % 3) switch
        {
            0 => "ADR",
            1 => "Runbook",
            _ => "Reference",
        };
        var subject = TechSubjects[rng.Next(TechSubjects.Length)];
        var year = FiscalYears[rng.Next(FiscalYears.Length)];

        string title;
        var body = new StringBuilder();

        if (docType == "ADR")
        {
            var decision = AdrDecisions[rng.Next(AdrDecisions.Length)];
            title = $"ADR-{index:D4}: {decision}";

            body.AppendLine("## Status").AppendLine().AppendLine("Accepted").AppendLine();
            body.AppendLine("## Context").AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture,
                $"The {subject} team needs to make a durable decision about the underlying technology choice. Pressure for the change comes from both throughput requirements and operational cost.").AppendLine();
            body.AppendLine("## Decision").AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture, $"We will {decision.ToLowerInvariant()}.").AppendLine();
            body.AppendLine("## Consequences").AppendLine();
            body.AppendLine("- Migration cost: ~3 sprints with the platform team.");
            body.AppendLine("- Reduced operational complexity once cutover completes.");
            body.AppendLine("- Existing dashboards and runbooks need refresh.");
        }
        else if (docType == "Runbook")
        {
            title = $"Runbook: {subject} on-call response";

            body.AppendLine("## When you are paged").AppendLine();
            body.AppendLine("1. Acknowledge the alert in PagerDuty within 5 minutes.");
            body.AppendLine("2. Open the live Grafana board linked from the alert payload.");
            body.AppendLine("3. Identify the saturation: error rate, p95 latency, or queue depth.");
            body.AppendLine();
            body.AppendLine("## Common scenarios").AppendLine();
            body.AppendLine("### Elevated p95 latency");
            body.AppendLine($"- Check downstream dependencies of the {subject}.");
            body.AppendLine("- Confirm pod count is at the expected baseline.");
            body.AppendLine();
            body.AppendLine("### Queue backlog");
            body.AppendLine("- Scale workers to 2× steady state.");
            body.AppendLine("- Drain poison-pill messages into the dead-letter queue.");
            body.AppendLine();
            body.AppendLine("## Escalation");
            body.AppendLine("If unresolved within 30 minutes, page the Platform Engineering lead.");
        }
        else
        {
            title = $"{subject} — API Reference";

            body.AppendLine(CultureInfo.InvariantCulture, $"The {subject} exposes a small REST surface used by internal tooling. Authentication is via short-lived OAuth tokens.").AppendLine();
            body.AppendLine("## Endpoints").AppendLine();
            body.AppendLine("### `GET /v1/items`");
            body.AppendLine("Returns a paginated list of items. Query parameters:");
            body.AppendLine("- `page` (int, default 1)");
            body.AppendLine("- `pageSize` (int, max 100, default 20)");
            body.AppendLine();
            body.AppendLine("### `POST /v1/items`");
            body.AppendLine("Creates a new item. Body is `application/json`. Returns `201 Created` with the new resource URL in the `Location` header.");
            body.AppendLine();
            body.AppendLine("## Rate limiting");
            var rps = 100 + rng.Next(0, 11) * 50;
            body.AppendLine(CultureInfo.InvariantCulture, $"The service enforces {rps} requests per second per tenant. Exceeding the limit returns `429 Too Many Requests` with a `Retry-After` header.");
        }

        return new SyntheticDocument(
            Id: $"tech-{index:D3}",
            Silo: "technical-docs",
            Department: "Engineering",
            Office: Offices[rng.Next(Offices.Length)],
            ConfidentialityLevel: "Internal",
            DocumentType: docType,
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: RandomDate(rng, year),
            Title: title,
            Body: body.ToString());
    }

    // ---------------------------------------------------------------------
    // Financial Reports
    // ---------------------------------------------------------------------
    public static SyntheticDocument FinancialReport(Random rng, int index)
    {
        var year = FiscalYears[rng.Next(FiscalYears.Length)];
        var quarter = $"Q{rng.Next(1, 5)}";
        var title = $"Quarterly Financial Report — {quarter} FY{year}";

        var revenue = 12_000_000 + rng.Next(0, 8_000_000);
        var growthPct = -5 + rng.NextDouble() * 25;
        var operatingMargin = 8 + rng.NextDouble() * 12;

        var body = new StringBuilder();
        body.AppendLine("## Executive Summary").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture,
            $"Contoso Intelligent Systems closed {quarter} FY{year} with revenue of CAD {revenue:N0}, a {growthPct:F1}% change year-over-year. Operating margin landed at {operatingMargin:F1}%.").AppendLine();

        body.AppendLine("## Revenue by Office").AppendLine();
        body.AppendLine("| Office     | Revenue (CAD) | % of Total |");
        body.AppendLine("|------------|---------------|------------|");
        var mtl = (long)(revenue * (0.42 + rng.NextDouble() * 0.06));
        var par = (long)(revenue * (0.32 + rng.NextDouble() * 0.05));
        var cas = revenue - mtl - par;
        body.AppendLine(CultureInfo.InvariantCulture, $"| Montreal   | {mtl,13:N0} | {100.0 * mtl / revenue,6:F1}%    |");
        body.AppendLine(CultureInfo.InvariantCulture, $"| Paris      | {par,13:N0} | {100.0 * par / revenue,6:F1}%    |");
        body.AppendLine(CultureInfo.InvariantCulture, $"| Casablanca | {cas,13:N0} | {100.0 * cas / revenue,6:F1}%    |");
        body.AppendLine();

        body.AppendLine("## Key Drivers").AppendLine();
        body.AppendLine("- Net new logos: " + (3 + rng.Next(0, 8)));
        body.AppendLine("- Existing-account expansion: positive");
        body.AppendLine("- Currency headwinds: minor");
        body.AppendLine();

        body.AppendLine("## Outlook").AppendLine();
        body.AppendLine("Forward indicators remain on plan. The pipeline coverage ratio for the next quarter is healthy. Engineering and Sales hiring remain the main investment lines.");

        return new SyntheticDocument(
            Id: $"fin-{index:D3}",
            Silo: "financial-reports",
            Department: "Finance",
            Office: "Montreal",
            ConfidentialityLevel: "Restricted",
            DocumentType: "Report",
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: RandomDate(rng, year),
            Title: title,
            Body: body.ToString());
    }

    // ---------------------------------------------------------------------
    // Legal Contracts (Article / Section / Clause hierarchy — for Ch 16 vectorless)
    // ---------------------------------------------------------------------
    private static readonly string[] ContractTypes = { "Master Services Agreement", "Statement of Work", "Non-Disclosure Agreement", "Reseller Agreement", "Data Processing Addendum" };
    private static readonly string[] Counterparties =
    {
        "Acme Holdings Ltd.", "Globex Industries", "Initech S.A.S.", "Soylent Corp.",
        "Umbrella Pharmaceuticals", "Stark Industries", "Wayne Enterprises", "Tyrell Corporation",
        "Pied Piper Inc.", "Wonka Industries",
    };

    public static SyntheticDocument LegalContract(Random rng, int index)
    {
        var contractType = ContractTypes[rng.Next(ContractTypes.Length)];
        var counterparty = Counterparties[rng.Next(Counterparties.Length)];
        var year = FiscalYears[rng.Next(FiscalYears.Length)];

        var title = $"{contractType} between Contoso Intelligent Systems and {counterparty}";

        var body = new StringBuilder();

        body.AppendLine("## Article 1 — Definitions").AppendLine();
        body.AppendLine("### Section 1.1 — Parties").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture,
            $"The parties to this {contractType} (the \"Agreement\") are Contoso Intelligent Systems, a Quebec corporation (\"Contoso\"), and {counterparty} (the \"Counterparty\").").AppendLine();
        body.AppendLine("### Section 1.2 — Scope").AppendLine();
        body.AppendLine("This Agreement governs the relationship between the parties for the services and deliverables described in Schedule A.").AppendLine();

        body.AppendLine("## Article 2 — Obligations").AppendLine();
        body.AppendLine("### Section 2.1 — Deliverables").AppendLine();
        body.AppendLine("Contoso shall provide the deliverables described in Schedule A according to the milestones in Schedule B.").AppendLine();
        body.AppendLine("### Section 2.2 — Acceptance Criteria").AppendLine();
        body.AppendLine("Acceptance occurs upon written confirmation by the Counterparty within ten (10) business days of delivery, or by lapse of the same period without rejection.").AppendLine();

        body.AppendLine("## Article 3 — Compensation").AppendLine();
        var fee = 50_000 + rng.Next(0, 20) * 25_000;
        body.AppendLine("### Section 3.1 — Fees").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"The Counterparty shall pay Contoso a fixed fee of CAD {fee:N0}, invoiced upon completion of each milestone.").AppendLine();
        body.AppendLine("### Section 3.2 — Late Payment").AppendLine();
        body.AppendLine("Invoices unpaid after thirty (30) days accrue interest at one and one-half percent (1.5%) per month, compounded monthly.").AppendLine();

        body.AppendLine("## Article 4 — Term and Termination").AppendLine();
        body.AppendLine("### Section 4.1 — Term").AppendLine();
        var termMonths = 12 + rng.Next(0, 4) * 6;
        body.AppendLine(CultureInfo.InvariantCulture, $"This Agreement is effective from the date of last signature and remains in force for {termMonths} months unless terminated earlier in accordance with this Article.").AppendLine();
        body.AppendLine("### Section 4.2 — Termination for Convenience").AppendLine();
        body.AppendLine("Either party may terminate this Agreement on sixty (60) days' written notice. Fees accrued before the termination date remain payable.").AppendLine();
        body.AppendLine("### Section 4.3 — Termination for Cause").AppendLine();
        body.AppendLine("Either party may terminate immediately if the other commits a material breach not remedied within thirty (30) days of written notice.").AppendLine();

        body.AppendLine("## Article 5 — Confidentiality").AppendLine();
        body.AppendLine("### Section 5.1 — Definition").AppendLine();
        body.AppendLine("Confidential Information includes any non-public information disclosed by one party to the other under this Agreement.").AppendLine();
        body.AppendLine("### Section 5.2 — Survival").AppendLine();
        body.AppendLine("The obligations of this Article survive termination of this Agreement for a period of five (5) years.").AppendLine();

        body.AppendLine("## Article 6 — Governing Law").AppendLine();
        body.AppendLine("This Agreement is governed by the laws of the Province of Quebec, without regard to its conflict-of-laws rules.");

        return new SyntheticDocument(
            Id: $"legal-{index:D3}",
            Silo: "legal-contracts",
            Department: "Legal",
            Office: "Montreal",
            ConfidentialityLevel: "Confidential",
            DocumentType: "Contract",
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: RandomDate(rng, year),
            Title: title,
            Body: body.ToString());
    }

    // ---------------------------------------------------------------------
    // Product Catalog
    // ---------------------------------------------------------------------
    private static readonly string[] ProductLines = { "SmartDocs", "SmartFlow", "SmartGuard", "SmartScale" };
    private static readonly string[] Tiers = { "Starter", "Team", "Business", "Enterprise" };

    public static SyntheticDocument ProductCatalog(Random rng, int index)
    {
        var product = ProductLines[rng.Next(ProductLines.Length)];
        var tier = Tiers[rng.Next(Tiers.Length)];
        var year = FiscalYears[rng.Next(FiscalYears.Length)];

        var title = $"{product} {tier} — Specification (FY{year})";

        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture,
            $"{product} {tier} is the {tier.ToLowerInvariant()} edition of Contoso's {product} product line for fiscal year {year}.").AppendLine();

        body.AppendLine("## Included Features").AppendLine();
        var features = new[] { "Single sign-on", "Audit log", "Custom roles", "API access", "SLA-backed support", "Dedicated tenant", "Bring-your-own-key encryption", "Sandbox environment" };
        var featureCount = 3 + rng.Next(0, 4);
        for (int i = 0; i < featureCount; i++)
        {
            body.Append("- ").AppendLine(features[(i + index) % features.Length]);
        }
        body.AppendLine();

        body.AppendLine("## Pricing").AppendLine();
        var pricePerSeat = tier switch
        {
            "Starter" => 12,
            "Team" => 28,
            "Business" => 64,
            _ => 0,
        };
        if (pricePerSeat > 0)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"CAD {pricePerSeat} per seat per month, billed annually. Volume discounts apply above 100 seats.");
        }
        else
        {
            body.AppendLine("Custom pricing. Contact a Contoso account executive.");
        }
        body.AppendLine();

        body.AppendLine("## Limits").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"- Documents: up to {(tier == "Enterprise" ? "unlimited" : (1000 * (rng.Next(1, 11))).ToString(CultureInfo.InvariantCulture))} per workspace.");
        body.AppendLine(CultureInfo.InvariantCulture, $"- Storage: {10 * (rng.Next(1, 11))} GB per seat.");
        body.AppendLine();

        body.AppendLine("## Compatibility").AppendLine();
        body.AppendLine("Available on Web, macOS, Windows, and the Contoso mobile app for iOS and Android.");

        return new SyntheticDocument(
            Id: $"prod-{index:D3}",
            Silo: "product-catalog",
            Department: "Product",
            Office: Offices[rng.Next(Offices.Length)],
            ConfidentialityLevel: "Public",
            DocumentType: "Specification",
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: RandomDate(rng, year),
            Title: title,
            Body: body.ToString());
    }

    // ---------------------------------------------------------------------
    // Release Notes & Support Tickets (the 6th silo — for Ch 22 drift)
    // ---------------------------------------------------------------------
    public static SyntheticDocument ReleaseNoteOrTicket(Random rng, int index)
    {
        // Half release notes, half tickets — alternating to make the temporal
        // evolution structure obvious to anyone browsing the silo.
        var isReleaseNote = (index % 2) == 0;
        var product = ProductLines[rng.Next(ProductLines.Length)];
        var year = FiscalYears[rng.Next(FiscalYears.Length)];
        var minor = rng.Next(1, 25);
        var patch = rng.Next(0, 6);

        string title;
        var body = new StringBuilder();
        string docType;
        DateOnly lastModified;

        if (isReleaseNote)
        {
            title = $"{product} {year}.{minor}.{patch} — Release Notes";
            docType = "ReleaseNote";
            lastModified = RandomDate(rng, year);

            body.AppendLine("## What's new").AppendLine();
            string[] features =
            {
                "Hybrid retrieval performance improvements (~25% lower p95).",
                "New audit-log export endpoint with cursor-based pagination.",
                "Tenant-level rate limiting now configurable from the admin console.",
                "Improved citation rendering in the answer pane.",
                "Faster cold-start on Apple Silicon.",
            };
            for (int i = 0; i < 3; i++)
            {
                body.Append("- ").AppendLine(features[(i + index) % features.Length]);
            }
            body.AppendLine();
            body.AppendLine("## Fixes").AppendLine();
            body.AppendLine("- Resolved a race condition in the embedding cache.");
            body.AppendLine("- Tightened input validation on the document upload endpoint.");
            body.AppendLine();
            body.AppendLine("## Known issues").AppendLine();
            body.AppendLine($"- Customers on offline-only deployments must run `{product.ToLowerInvariant()} migrate --vector-index` after upgrade.");
        }
        else
        {
            var ticketId = 10000 + index * 7;
            title = $"Support Ticket #{ticketId} — {product}";
            docType = "Ticket";
            lastModified = RandomDate(rng, year);

            body.AppendLine("## Reported by").AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture, $"{Counterparties[rng.Next(Counterparties.Length)]} on the {product} {tierBand(rng)} plan.").AppendLine();

            body.AppendLine("## Summary").AppendLine();
            string[] summaries =
            {
                "Embedding latency spikes during peak hours.",
                "Citations occasionally drop the page-number field.",
                "Audit log entries missing for SSO-driven document access.",
                "Hybrid retrieval returning duplicates after re-indexing.",
                "Streaming responses cut off mid-token on slow networks.",
            };
            body.AppendLine(summaries[index % summaries.Length]).AppendLine();

            body.AppendLine("## Steps to reproduce").AppendLine();
            body.AppendLine("1. Upload a 50-page PDF.");
            body.AppendLine("2. Issue the query \"Summarize section 4\".");
            body.AppendLine("3. Observe the response stream.").AppendLine();

            body.AppendLine("## Resolution").AppendLine();
            string[] resolutions =
            {
                "Hotfixed in the next minor release.",
                "Workaround documented; permanent fix tracked in JIRA.",
                "Customer was on a deprecated config; migrated and verified.",
                "Closed as not-reproducible; auto-reopen if it recurs.",
            };
            body.AppendLine(resolutions[index % resolutions.Length]);
        }

        return new SyntheticDocument(
            Id: $"rn-{index:D3}",
            Silo: "release-notes-tickets",
            Department: "Engineering",
            Office: Offices[rng.Next(Offices.Length)],
            ConfidentialityLevel: "Internal",
            DocumentType: docType,
            FiscalYear: int.Parse(year, CultureInfo.InvariantCulture),
            Author: Authors[rng.Next(Authors.Length)],
            LastModified: lastModified,
            Title: title,
            Body: body.ToString());
    }

    private static string tierBand(Random rng) => Tiers[rng.Next(Tiers.Length)];

    // Deterministic random date inside the given fiscal year.
    private static DateOnly RandomDate(Random rng, string fiscalYear)
    {
        var y = int.Parse(fiscalYear, CultureInfo.InvariantCulture);
        var startDay = new DateOnly(y, 1, 1).DayNumber;
        var endDay = new DateOnly(y, 12, 31).DayNumber;
        var pick = startDay + rng.Next(0, endDay - startDay + 1);
        return DateOnly.FromDayNumber(pick);
    }
}
