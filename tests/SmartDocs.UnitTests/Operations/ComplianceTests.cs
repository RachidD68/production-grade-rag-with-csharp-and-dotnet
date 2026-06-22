using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Generation.Citations;
using SmartDocs.Mcp;
using SmartDocs.Operations;
using SmartDocs.Operations.Compliance;
using SmartDocs.Security;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.UnitTests.Operations;

public sealed class ComplianceTests
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("unit-test-key");

    /// <summary>
    /// Minimal controllable <see cref="TimeProvider"/> for deterministic tests
    /// (no Microsoft.Extensions.TimeProvider.Testing package in this repo).
    /// </summary>
    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static DocumentMetadata Meta(string id, string title = "Title") => new(
        id, "hr-policies", "HR", "Montreal", "Internal", "Policy",
        2026, "author", new DateOnly(2026, 5, 10), title);

    private static DocumentChunk Chunk(string docId, int index, string text, HmacProvenanceSigner? signer = null)
    {
        var chunk = new DocumentChunk($"{docId}#{index}", docId, index, text, 0, text.Length, Meta(docId));
        return signer is null ? chunk : chunk with { Provenance = signer.Sign(chunk) };
    }

    // --- CitationAuditor ---

    [Fact]
    public async Task CitationAuditor_traces_chain_with_verifying_signatures()
    {
        var signer = new HmacProvenanceSigner(Key);
        var chunk = Chunk("hr-001", 0, "Employees get 25 vacation days.", signer);

        var lookup = new InMemoryChunkLookup([chunk]);
        var docs = new InMemoryDocumentMetadataStore();
        docs.Put(chunk.Metadata);

        var audit = new InMemoryAuditRecordStore();
        var citation = new Citation("claim", 1, chunk.ChunkId, chunk.DocumentId, "Title", "SUPPORTED");
        audit.Put("q-1", new AuditEntry(
            DateTimeOffset.UtcNow, "How many days?", [chunk.ChunkId], "25 days. [Source 1]",
            [citation], 0.95, "user-1"));

        var auditor = new CitationAuditor(audit, lookup, docs, signer);
        var trace = await auditor.TraceAsync("q-1");

        Assert.Equal("q-1", trace.QueryId);
        Assert.Equal("25 days. [Source 1]", trace.Answer);
        var step = Assert.Single(trace.Chain);
        Assert.Equal(1, step.CitationN);
        Assert.Equal("hr-001#0", step.ChunkId);
        Assert.Equal("hr-001", step.DocumentId);
        Assert.Equal("smartdocs://doc/hr-001", step.SourceUri);
        Assert.True(step.SignatureValid);
        Assert.Equal(signer.Sign(chunk), step.ProvenanceSignature);
    }

    [Fact]
    public async Task CitationAuditor_flags_tampered_chunk_as_invalid()
    {
        var signer = new HmacProvenanceSigner(Key);
        // Sign the ORIGINAL text, then tamper the chunk text after signing.
        var original = Chunk("hr-001", 0, "Employees get 25 vacation days.", signer);
        var tampered = original with { Text = "Employees get 50 vacation days." };

        var lookup = new InMemoryChunkLookup([tampered]);
        var docs = new InMemoryDocumentMetadataStore();
        docs.Put(tampered.Metadata);

        var audit = new InMemoryAuditRecordStore();
        var citation = new Citation("claim", 1, tampered.ChunkId, tampered.DocumentId, "Title", "SUPPORTED");
        audit.Put("q-1", new AuditEntry(
            DateTimeOffset.UtcNow, "How many days?", [tampered.ChunkId], "answer",
            [citation], 0.5, null));

        var auditor = new CitationAuditor(audit, lookup, docs, signer);
        var trace = await auditor.TraceAsync("q-1");

        var step = Assert.Single(trace.Chain);
        Assert.False(step.SignatureValid);
    }

    [Fact]
    public async Task CitationAuditor_throws_for_unknown_query()
    {
        var signer = new HmacProvenanceSigner(Key);
        var auditor = new CitationAuditor(
            new InMemoryAuditRecordStore(), new InMemoryChunkLookup(), new InMemoryDocumentMetadataStore(), signer);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => auditor.TraceAsync("missing"));
    }

    [Fact]
    public async Task CitationAuditor_marks_erased_chunk_invalid()
    {
        var signer = new HmacProvenanceSigner(Key);
        var audit = new InMemoryAuditRecordStore();
        var citation = new Citation("claim", 2, "gone#0", "gone", "Title", "SUPPORTED");
        audit.Put("q-1", new AuditEntry(
            DateTimeOffset.UtcNow, "q", ["gone#0"], "a", [citation], 0.0, null));

        // Empty lookup: the cited chunk was erased.
        var auditor = new CitationAuditor(audit, new InMemoryChunkLookup(), new InMemoryDocumentMetadataStore(), signer);
        var trace = await auditor.TraceAsync("q-1");

        var step = Assert.Single(trace.Chain);
        Assert.False(step.SignatureValid);
        Assert.Equal("gone#0", step.ChunkId);
        Assert.Equal(string.Empty, step.ChunkText);
    }

    // --- ModelCardGenerator ---

    private sealed class StubModelRegistry(IReadOnlyList<ModelEntry> models) : IModelRegistry
    {
        public Task<IReadOnlyList<ModelEntry>> GetModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(models);
    }

    private sealed class StubCorpusInventory(IReadOnlyList<DataSourceEntry> sources) : ICorpusInventory
    {
        public Task<IReadOnlyList<DataSourceEntry>> GetDataSourcesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(sources);
    }

    private static ModelCardGenerator BuildCardGenerator(out Func<byte[], string> sign)
    {
        var models = new StubModelRegistry([new("chat", "gpt-4o", "2024-11-20")]);
        var corpus = new StubCorpusInventory([new("hr-policies", 42, "Internal")]);
        sign = ModelCardGenerator.HmacSigner(Key);
        var profile = new ModelCardProfile(
            "SmartDocs RAG", "2.1.0", "Answer HR questions.",
            [new MetricEntry("faithfulness", 0.94)],
            ["May abstain."],
            ["Audit trace on every answer."]);
        return new ModelCardGenerator(models, corpus, new MarkdownModelCardRenderer(), sign, profile);
    }

    [Fact]
    public async Task ModelCardGenerator_renders_live_models_and_signature_verifies()
    {
        var gen = BuildCardGenerator(out var sign);
        var (markdown, signature) = await gen.GenerateSignedAsync();

        Assert.Contains("gpt-4o", markdown, StringComparison.Ordinal);
        Assert.Contains("2024-11-20", markdown, StringComparison.Ordinal); // live model version
        Assert.Contains("faithfulness", markdown, StringComparison.Ordinal);
        Assert.Contains("hr-policies", markdown, StringComparison.Ordinal);

        // The detached signature recomputes over the rendered bytes.
        var expected = sign(Encoding.UTF8.GetBytes(markdown));
        Assert.Equal(expected, signature);
    }

    [Fact]
    public async Task ModelCardGenerator_builds_structured_card()
    {
        var gen = BuildCardGenerator(out _);
        var card = await gen.BuildAsync();

        Assert.Equal("SmartDocs RAG", card.SystemName);
        Assert.Single(card.Models);
        Assert.Equal("gpt-4o", card.Models[0].Name);
        Assert.Single(card.DataSources);
        Assert.Single(card.PerformanceMetrics);
    }

    // --- ProductionFaithfulnessSampler ---

    private sealed class FixedJudge(double score) : IFaithfulnessJudge
    {
        public Task<double> ScoreAsync(ProductionAnswer answer, CancellationToken cancellationToken = default) =>
            Task.FromResult(score);
    }

    /// <summary>A judge whose next score can be set between offers.</summary>
    private sealed class MutableJudge(double initial) : IFaithfulnessJudge
    {
        public double NextScore { get; set; } = initial;
        public Task<double> ScoreAsync(ProductionAnswer answer, CancellationToken cancellationToken = default) =>
            Task.FromResult(NextScore);
    }

    private sealed class CapturingAlertSink : IFaithfulnessAlertSink
    {
        public List<FaithfulnessAlert> Alerts { get; } = [];
        public Task AlertAsync(FaithfulnessAlert alert, CancellationToken cancellationToken = default)
        {
            Alerts.Add(alert);
            return Task.CompletedTask;
        }
    }

    private static ProductionAnswer Answer(string id) => new(id, "q", "a", []);

    [Fact]
    public async Task FaithfulnessSampler_alerts_when_rolling_average_below_threshold()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new CapturingAlertSink();
        // Judge always returns 0.50 (below 0.82). SampleRate 1.0 + roll 0.0 ⇒ always sampled.
        var sampler = new ProductionFaithfulnessSampler(
            new FixedJudge(0.50), sink,
            new FaithfulnessSamplerOptions(SampleRate: 1.0, Threshold: 0.82, WindowDays: 7),
            clock,
            nextSampleRoll: () => 0.0);

        var sample = await sampler.OfferAsync(Answer("q-1"));

        Assert.NotNull(sample);
        Assert.Equal(0.50, sample.Score);
        var alert = Assert.Single(sink.Alerts);
        Assert.True(alert.RollingAverage < alert.Threshold);
        Assert.Equal(1, alert.WindowSampleCount);
    }

    [Fact]
    public async Task FaithfulnessSampler_does_not_alert_when_above_threshold()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new CapturingAlertSink();
        var sampler = new ProductionFaithfulnessSampler(
            new FixedJudge(0.95), sink,
            new FaithfulnessSamplerOptions(SampleRate: 1.0, Threshold: 0.82, WindowDays: 7),
            clock,
            nextSampleRoll: () => 0.0);

        await sampler.OfferAsync(Answer("q-1"));

        Assert.Empty(sink.Alerts);
        Assert.Single(sampler.Samples);
    }

    [Fact]
    public async Task FaithfulnessSampler_skips_when_roll_above_rate()
    {
        var sink = new CapturingAlertSink();
        // roll 0.99 >= rate 0.005 ⇒ never sampled.
        var sampler = new ProductionFaithfulnessSampler(
            new FixedJudge(0.10), sink,
            new FaithfulnessSamplerOptions(SampleRate: 0.005),
            nextSampleRoll: () => 0.99);

        var sample = await sampler.OfferAsync(Answer("q-1"));

        Assert.Null(sample);
        Assert.Empty(sampler.Samples);
        Assert.Empty(sink.Alerts);
    }

    [Fact]
    public async Task FaithfulnessSampler_excludes_samples_outside_window()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var sink = new CapturingAlertSink();
        var judge = new MutableJudge(0.50); // first sample is low
        var sampler = new ProductionFaithfulnessSampler(
            judge, sink,
            new FaithfulnessSamplerOptions(SampleRate: 1.0, Threshold: 0.82, WindowDays: 7),
            clock,
            nextSampleRoll: () => 0.0);

        // First low sample fires an alert.
        await sampler.OfferAsync(Answer("q-old"));
        Assert.Single(sink.Alerts);

        // Advance 10 days (outside the 7-day window) and offer a high sample. The
        // old low sample is now outside the window so the rolling average is just
        // the new high score (0.95) — above the 0.82 floor — and no new alert fires.
        clock.Advance(TimeSpan.FromDays(10));
        sink.Alerts.Clear();
        judge.NextScore = 0.95;
        await sampler.OfferAsync(Answer("q-new"));

        Assert.Empty(sink.Alerts);
        Assert.Equal(2, sampler.Samples.Count); // both recorded, but only one in the window
    }

    // --- IncidentRegister ---

    [Fact]
    public async Task IncidentRegister_records_and_queries_incidents()
    {
        var register = new IncidentRegister(new InMemoryIncidentStore());
        await register.RecordAsync(new Incident(
            "inc-1", IncidentSeverity.High, "tenant-a", "bad filter", "patched",
            ["log:123"], DateTimeOffset.UtcNow, Resolved: false));
        await register.RecordAsync(new Incident(
            "inc-2", IncidentSeverity.Low, "global", "typo", "fixed",
            ["log:456"], DateTimeOffset.UtcNow, Resolved: true));

        var all = await register.QueryAsync();
        Assert.Equal(2, all.Count);

        var open = await register.OpenIncidentsAsync();
        var openOne = Assert.Single(open);
        Assert.Equal("inc-1", openOne.IncidentId);

        var report = await register.ReportAsync();
        Assert.Equal(2, report.Total);
        Assert.Equal(1, report.Open);
        Assert.Equal(1, report.BySeverity[IncidentSeverity.High]);
    }

    [Fact]
    public async Task IncidentRegister_adapts_security_incident()
    {
        var register = new IncidentRegister(new InMemoryIncidentStore());
        var security = new SecurityIncident("cross-tenant-leak", "blocked chunk from tenant-b", DateTimeOffset.UtcNow);

        var incident = await register.RecordSecurityIncidentAsync(security);

        Assert.Equal(IncidentSeverity.High, incident.Severity);
        Assert.Equal("cross-tenant-leak", incident.Scope);
        Assert.Equal("blocked chunk from tenant-b", incident.RootCause);
        var stored = Assert.Single(await register.QueryAsync());
        Assert.Equal(incident.IncidentId, stored.IncidentId);
    }

    // --- ConsentRegistry ---

    [Fact]
    public async Task ConsentRegistry_per_scope_revoke_leaves_other_scopes_intact()
    {
        var registry = new ConsentRegistry();
        await registry.RecordAsync("alice", "core-search", "v3");
        await registry.RecordAsync("alice", "analytics", "v3");

        var revoked = await registry.RevokeAsync("alice", "analytics");
        Assert.True(revoked);

        var grants = await registry.GetCurrentAsync("alice");
        Assert.Equal(2, grants.Count);
        Assert.True(grants.Single(g => g.Scope == "core-search").Granted);
        Assert.False(grants.Single(g => g.Scope == "analytics").Granted);
    }

    [Fact]
    public async Task ConsentRegistry_revoke_unknown_scope_is_noop()
    {
        var registry = new ConsentRegistry();
        await registry.RecordAsync("alice", "core-search", "v3");
        Assert.False(await registry.RevokeAsync("alice", "nope"));
        Assert.False(await registry.RevokeAsync("bob", "core-search"));
    }

    [Fact]
    public async Task ConsentRegistry_coverage_counts_active_grants()
    {
        var registry = new ConsentRegistry();
        await registry.RecordAsync("alice", "core-search", "v3");
        await registry.RecordAsync("bob", "core-search", "v3");
        await registry.RevokeAsync("bob", "core-search");

        var coverage = await registry.CoverageAsync(["alice", "bob", "carol"], "core-search");
        Assert.Equal(1, coverage.Covered);
        Assert.Equal(3, coverage.Total);
        Assert.Equal(1.0 / 3.0, coverage.Fraction, precision: 6);
    }

    // --- ComplianceDashboard ---

    [Fact]
    public void ComplianceDashboard_projects_six_tiles()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var dashboard = new ComplianceDashboard(clock);
        var metrics = new ComplianceMetrics(
            FaithfulnessToday: 0.94,
            Faithfulness7DayTrend: 0.92,
            PiiRedactionRate: 0.99,
            TenantLeakPreventionCount: 3,
            ActiveErasureRequests: 2,
            ConsentCoverage: new ConsentCoverage("core-search", 8, 10, 0.8),
            OpenIncidents: 1);

        var snapshot = dashboard.Build(metrics);

        Assert.Equal(6, snapshot.Tiles.Count);
        Assert.Equal("Faithfulness (today)", snapshot.Faithfulness.Title);
        Assert.Equal("0.94", snapshot.Faithfulness.Value);
        Assert.Equal("faithfulness/samples", snapshot.Faithfulness.LinkKey);
        Assert.Equal("1", snapshot.OpenIncidents.Value);
        Assert.Equal("incidents/open", snapshot.OpenIncidents.LinkKey);
        Assert.All(snapshot.Tiles, t => Assert.False(string.IsNullOrEmpty(t.LinkKey)));
    }

    // --- ExplanationPanel ---

    private static ExplanationContext PanelContext() => new(
        "Answer text.",
        0.88,
        [new Citation("claim", 1, "hr-001#0", "hr-001", "Title", "SUPPORTED")],
        ["retrieved chunk hr-001#0", "grounded answer on it"],
        "gpt-4o",
        "2024-11-20");

    [Fact]
    public void ExplanationPanel_limited_risk_renders_chips_only()
    {
        var panel = new ExplanationPanel(new FixedRiskClassifier(RiskClassification.Limited));
        var view = panel.Render(PanelContext());

        Assert.False(view.ShowFullPanel);
        Assert.NotEmpty(view.Chips);
        Assert.Empty(view.CitedChunks);
        Assert.Empty(view.ReasoningChain);
        Assert.Null(view.ModelVersion);
        Assert.Null(view.RequestHumanReviewAction);
    }

    [Fact]
    public void ExplanationPanel_high_risk_renders_full_panel()
    {
        var panel = new ExplanationPanel(new FixedRiskClassifier(RiskClassification.High));
        var view = panel.Render(PanelContext());

        Assert.True(view.ShowFullPanel);
        Assert.NotEmpty(view.Chips);
        Assert.Single(view.CitedChunks);
        Assert.Equal(2, view.ReasoningChain.Count);
        Assert.Equal("gpt-4o", view.ModelName);
        Assert.Equal("2024-11-20", view.ModelVersion);
        Assert.Equal(ExplanationPanel.RequestHumanReviewActionKey, view.RequestHumanReviewAction);
    }

    // --- HumanReviewQueue ---

    [Fact]
    public void HumanReviewQueue_enqueue_dequeue_override_captures_eval_case()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var queue = new HumanReviewQueue(clock);

        var item = queue.Enqueue("q-1", "How many days?", "Wrong answer.", 0.4, "low-confidence");
        Assert.Equal(1, queue.PendingCount);

        var dequeued = queue.Dequeue();
        Assert.NotNull(dequeued);
        Assert.Equal(item.ItemId, dequeued.ItemId);
        Assert.False(dequeued.IsResolved);

        var resolved = queue.Override(item.ItemId, "Correct: 25 days.");
        Assert.NotNull(resolved);
        Assert.True(resolved.IsResolved);
        Assert.Equal("Correct: 25 days.", resolved.ReviewerEdit);

        var cases = queue.ResolvedCases;
        var evalCase = Assert.Single(cases);
        Assert.Equal("How many days?", evalCase.Query);
        Assert.Equal("Correct: 25 days.", evalCase.ReviewerEdit);
    }

    [Fact]
    public void HumanReviewQueue_dequeue_empty_returns_null()
    {
        var queue = new HumanReviewQueue();
        Assert.Null(queue.Dequeue());
        Assert.Null(queue.Override("nope", "edit"));
    }

    // --- ErasureReceipt ---

    [Fact]
    public void ErasureReceipt_built_from_deletion_signs_and_verifies()
    {
        var received = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var deletion = new DeletionAuditEntry(
            "usr-alice", ["usr-alice#0"], ["entity-alice"], "dpo@contoso.com",
            received.AddHours(2));

        var gen = new ErasureReceiptGenerator(ErasureReceiptGenerator.HmacSigner(Key));
        var receipt = gen.Build("req-1", received, deletion, redactedAuditRecords: 2);

        Assert.Equal("req-1", receipt.RequestId);
        Assert.Equal("usr-alice", receipt.SubjectId);
        Assert.True(receipt.SlaMet);
        Assert.Equal(3, receipt.Stores.Count); // vector + graph + audit-log
        Assert.Equal(1, receipt.Stores[0].RecordsErased);
        Assert.Equal(2, receipt.Stores[2].RecordsRedacted);
        Assert.NotEmpty(receipt.Signature);
        Assert.True(gen.Verify(receipt));
    }

    [Fact]
    public void ErasureReceipt_tampered_signature_fails_verification()
    {
        var received = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var deletion = new DeletionAuditEntry(
            "usr-alice", ["usr-alice#0"], [], "dpo", received.AddHours(1));
        var gen = new ErasureReceiptGenerator(ErasureReceiptGenerator.HmacSigner(Key));
        var receipt = gen.Build("req-1", received, deletion);

        var tampered = receipt with { SubjectId = "usr-mallory" };
        Assert.False(gen.Verify(tampered));
    }

    [Fact]
    public void ErasureReceipt_misses_sla_when_late()
    {
        var received = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var deletion = new DeletionAuditEntry(
            "usr-alice", [], [], "dpo", received.AddDays(45)); // beyond 30-day SLA
        var gen = new ErasureReceiptGenerator(ErasureReceiptGenerator.HmacSigner(Key));
        var receipt = gen.Build("req-1", received, deletion);
        Assert.False(receipt.SlaMet);
    }
}
