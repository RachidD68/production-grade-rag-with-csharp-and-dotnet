// Chapter 24 — Trust by Design.
//
// Demonstrates citation auditing and model card generation for RAG systems.
// Implements a CitationAuditor that traces a citation back through the RAG
// chain to its source, and a ModelCardGenerator that produces a JSON model
// card from system configuration.
//
// Run:
//   dotnet run --project samples/Ch24_TrustByDesign

using System.Text.Json;
using System.Text.Json.Serialization;

// Simulate a RAG chain that produced a citation.
var chain = new List<ChainStep>
{
    new(
        StepId: "step-001",
        StepType: "ingestion",
        Input: "HR_Policy_2026.pdf",
        Output: "Extracted 42 paragraphs from PDF",
        Timestamp: DateTimeOffset.Parse("2026-05-10T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        Metadata: new() { ["source_hash"] = "sha256:a1b2c3d4", ["page_range"] = "1-15" }),

    new(
        StepId: "step-002",
        StepType: "chunking",
        Input: "Paragraph 7: 'Employees are entitled to 25 vacation days per year...'",
        Output: "chunk-007: 'Employees are entitled to 25 vacation days per year. Unused days carry over up to 5.'",
        Timestamp: DateTimeOffset.Parse("2026-05-10T09:00:05Z", System.Globalization.CultureInfo.InvariantCulture),
        Metadata: new() { ["chunk_id"] = "chunk-007", ["strategy"] = "paragraph-level" }),

    new(
        StepId: "step-003",
        StepType: "embedding",
        Input: "chunk-007",
        Output: "vector stored at index 007 in Qdrant collection 'hr-policies'",
        Timestamp: DateTimeOffset.Parse("2026-05-10T09:00:06Z", System.Globalization.CultureInfo.InvariantCulture),
        Metadata: new() { ["model"] = "text-embedding-3-small", ["dimensions"] = "1536" }),

    new(
        StepId: "step-004",
        StepType: "retrieval",
        Input: "Query: 'How many vacation days do I get?'",
        Output: "Retrieved chunk-007 with score 0.94",
        Timestamp: DateTimeOffset.Parse("2026-05-17T14:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
        Metadata: new() { ["score"] = "0.94", ["rank"] = "1" }),

    new(
        StepId: "step-005",
        StepType: "generation",
        Input: "Context: chunk-007 | Query: 'How many vacation days do I get?'",
        Output: "You are entitled to 25 vacation days per year. [Source: HR_Policy_2026.pdf, p.3]",
        Timestamp: DateTimeOffset.Parse("2026-05-17T14:30:01Z", System.Globalization.CultureInfo.InvariantCulture),
        Metadata: new() { ["model"] = "gpt-4o", ["temperature"] = "0.1", ["tokens_used"] = "287" }),
};

Console.WriteLine("=== Ch24: Trust by Design ===");
Console.WriteLine();

// --- Citation Auditor ---

Console.WriteLine("--- Citation Auditor ---");
Console.WriteLine();

var trace = AuditCitation(
    citationId: "cite-001",
    claimedSource: "HR_Policy_2026.pdf",
    chain: chain);

Console.WriteLine($"Citation ID:     {trace.CitationId}");
Console.WriteLine($"Claimed Source:  {trace.ClaimedSource}");
Console.WriteLine($"Chain Length:    {trace.Chain.Count} steps");
Console.WriteLine($"Verified:        {trace.IsVerified}");
Console.WriteLine($"Note:            {trace.VerificationNote}");
Console.WriteLine();
Console.WriteLine("  Audit Chain:");
foreach (var step in trace.Chain)
{
    Console.WriteLine($"    [{step.Timestamp:HH:mm:ss}] {step.StepType,-12} -> {Truncate(step.Output, 60)}");
}

Console.WriteLine();

// --- Model Card Generator ---

Console.WriteLine("--- Model Card Generator ---");
Console.WriteLine();

var config = new SystemConfig(
    SystemName: "SmartDocs RAG",
    Version: "2.1.0",
    Provider: "Azure OpenAI",
    ModelId: "gpt-4o",
    MaxContextTokens: 128_000,
    Temperature: "0.1",
    VectorDatabase: "Qdrant",
    EmbeddingModel: "text-embedding-3-small",
    ChunkSize: 512,
    TopK: 5,
    RerankingEnabled: true);

var modelCard = GenerateModelCard(config);

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var json = JsonSerializer.Serialize(modelCard, jsonOptions);
Console.WriteLine("Generated Model Card (JSON):");
Console.WriteLine(json);

Console.WriteLine();
Console.WriteLine("Done.");
return;

// --- Citation Auditor implementation ---

static AuditTrace AuditCitation(string citationId, string claimedSource, IReadOnlyList<ChainStep> chain)
{
    // Verify the chain: check that the source appears in the ingestion step.
    var ingestionStep = chain.FirstOrDefault(s => s.StepType == "ingestion");
    var sourceVerified = ingestionStep?.Input.Contains(claimedSource, StringComparison.OrdinalIgnoreCase) ?? false;

    // Check chain integrity: each step's timestamp should be >= previous.
    var temporallyValid = true;
    for (var i = 1; i < chain.Count; i++)
    {
        if (chain[i].Timestamp < chain[i - 1].Timestamp)
        {
            temporallyValid = false;
            break;
        }
    }

    // Check that retrieval score meets minimum threshold.
    var retrievalStep = chain.FirstOrDefault(s => s.StepType == "retrieval");
    var scoreValid = true;
    if (retrievalStep is not null &&
        retrievalStep.Metadata.TryGetValue("score", out var scoreStr) &&
        float.TryParse(scoreStr, out var score))
    {
        scoreValid = score >= 0.7f;
    }

    var isVerified = sourceVerified && temporallyValid && scoreValid;
    var note = isVerified
        ? $"Citation verified: source '{claimedSource}' found in ingestion, chain temporally valid, retrieval score adequate."
        : BuildFailureNote(sourceVerified, temporallyValid, scoreValid);

    return new AuditTrace(citationId, claimedSource, chain, isVerified, note);
}

static string BuildFailureNote(bool sourceOk, bool temporalOk, bool scoreOk)
{
    var issues = new List<string>();
    if (!sourceOk)
    {
        issues.Add("source not found in ingestion step");
    }

    if (!temporalOk)
    {
        issues.Add("chain timestamps out of order");
    }

    if (!scoreOk)
    {
        issues.Add("retrieval score below threshold (0.7)");
    }

    return $"Verification failed: {string.Join("; ", issues)}.";
}

// --- Model Card Generator implementation ---

static ModelCard GenerateModelCard(SystemConfig config)
{
    return new ModelCard(
        SystemName: config.SystemName,
        Version: config.Version,
        GeneratedAt: DateTimeOffset.UtcNow,
        Model: new ModelDetails(
            config.Provider,
            config.ModelId,
            config.MaxContextTokens,
            config.Temperature),
        Retrieval: new RetrievalDetails(
            config.VectorDatabase,
            config.EmbeddingModel,
            config.ChunkSize,
            config.TopK,
            config.RerankingEnabled),
        KnownLimitations:
        [
            "May hallucinate when retrieval score is below 0.7",
            "Does not support multi-modal content (images, tables)",
            "Maximum document size limited to 100 pages",
            "Embedding model has 8191 token input limit per chunk",
        ],
        EthicalConsiderations:
        [
            "All generated answers include source citations for verifiability",
            "System logs audit traces for every citation produced",
            "PII detected in source documents is redacted before indexing",
            "Model outputs are constrained by guardrails to prevent harmful content",
        ]);
}

static string Truncate(string s, int max) =>
    s.Length <= max ? s : s[..max] + "...";

// --- Domain types (must follow top-level statements) ---

/// <summary>A single step in the RAG processing chain.</summary>
sealed record ChainStep(
    string StepId,
    string StepType,
    string Input,
    string Output,
    DateTimeOffset Timestamp,
    Dictionary<string, string> Metadata);

/// <summary>Full audit trace for a citation.</summary>
sealed record AuditTrace(
    string CitationId,
    string ClaimedSource,
    IReadOnlyList<ChainStep> Chain,
    bool IsVerified,
    string? VerificationNote);

/// <summary>Model card describing a RAG system configuration.</summary>
sealed record ModelCard(
    string SystemName,
    string Version,
    DateTimeOffset GeneratedAt,
    ModelDetails Model,
    RetrievalDetails Retrieval,
    IReadOnlyList<string> KnownLimitations,
    IReadOnlyList<string> EthicalConsiderations);

/// <summary>LLM model details for the model card.</summary>
sealed record ModelDetails(
    string Provider,
    string ModelId,
    int MaxContextTokens,
    string TemperatureSetting);

/// <summary>Retrieval configuration details for the model card.</summary>
sealed record RetrievalDetails(
    string VectorDatabase,
    string EmbeddingModel,
    int ChunkSize,
    int TopK,
    bool RerankingEnabled);

/// <summary>System configuration for model card generation.</summary>
sealed record SystemConfig(
    string SystemName,
    string Version,
    string Provider,
    string ModelId,
    int MaxContextTokens,
    string Temperature,
    string VectorDatabase,
    string EmbeddingModel,
    int ChunkSize,
    int TopK,
    bool RerankingEnabled);
