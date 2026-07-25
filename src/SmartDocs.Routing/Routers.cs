using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Abstractions;
using SmartDocs.Core.Numerics;

namespace SmartDocs.Routing;

/// <summary>
/// Keyword-based router. Cheap, predictable, no LLM call. Fast first
/// pass; fall through to a semantic router if confidence is low.
/// </summary>
public sealed class RuleBasedRouter : IQueryRouter
{
    public string Strategy => "rule-based";

    private static readonly FrozenDictionary<string, string[]> SiloKeywords = new Dictionary<string, string[]>
    {
        ["hr-policies"] = ["policy", "leave", "vacation", "sick", "remote", "parental", "salary", "probation", "onboarding"],
        ["technical-docs"] = ["api", "endpoint", "deploy", "service", "runbook", "adr", "architecture", "rate limit"],
        ["financial-reports"] = ["revenue", "q1", "q2", "q3", "q4", "budget", "forecast", "fiscal", "operating margin"],
        ["legal-contracts"] = ["contract", "nda", "sow", "clause", "terminate", "termination", "agreement", "license"],
        ["product-catalog"] = ["pricing", "tier", "feature", "starter", "team", "business", "enterprise", "specification"],
        ["release-notes-tickets"] = ["release notes", "release", "ticket", "regression", "hotfix", "support ticket"],
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The canonical six-silo set this corpus is partitioned into. The keys of
    /// <see cref="SiloKeywords"/> are the single source of truth; other routers
    /// (e.g. <see cref="LlmClassifierRouter"/>) validate model output against
    /// <see cref="KnownSilos"/> so a hallucinated silo name never reaches a retriever.
    /// </summary>
    public static FrozenSet<string> KnownSilos { get; } =
        SiloKeywords.Keys.ToFrozenSet(StringComparer.Ordinal);

    public Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var lower = query.ToLowerInvariant();
        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (silo, keywords) in SiloKeywords)
        {
            foreach (var kw in keywords)
            {
                if (lower.Contains(kw, StringComparison.Ordinal))
                {
                    hits[silo] = hits.TryGetValue(silo, out var n) ? n + 1 : 1;
                }
            }
        }
        if (hits.Count == 0)
        {
            return Task.FromResult(new RoutingDecision(
                Silos: Array.Empty<string>(),
                Confidence: 0,
                Reasoning: "no keyword overlap",
                Strategy: Strategy));
        }
        var maxHits = hits.Values.Max();
        var silos = hits.Where(kv => kv.Value == maxHits).Select(kv => kv.Key).ToArray();
        var confidence = Math.Min(1.0, maxHits * 0.25);
        return Task.FromResult(new RoutingDecision(
            Silos: silos,
            Confidence: confidence,
            Reasoning: $"keyword hits: {string.Join(", ", hits.Select(kv => $"{kv.Key}={kv.Value}"))}",
            Strategy: Strategy));
    }
}

/// <summary>
/// LLM-based router. Asks the chat model to classify the query into one
/// or more silos with a confidence score. Slower than rules but handles
/// novel phrasings. Distinct from <see cref="SemanticRouter"/>, which routes
/// by embedding similarity and never calls a chat model.
/// </summary>
/// <remarks>
/// The model is free-form text and can hallucinate silo names that do not
/// exist. <see cref="RouteAsync"/> therefore validates every returned silo
/// against <see cref="RuleBasedRouter.KnownSilos"/> and drops the rest; if the
/// model returns <em>only</em> unknown silos the decision collapses to an empty
/// silo set with low confidence.
/// </remarks>
public sealed class LlmClassifierRouter : IQueryRouter
{
    private readonly IChatClient _chat;
    public string Strategy => "llm-classifier";

    /// <summary>
    /// Canonical silo names the model is allowed to return. Reuses
    /// <see cref="RuleBasedRouter.KnownSilos"/> so both routers share one source
    /// of truth for the six-silo taxonomy.
    /// </summary>
    private static readonly FrozenSet<string> KnownSilos = RuleBasedRouter.KnownSilos;

    private const string Prompt =
        """
        Classify the following query into one or more of these silos:
          hr-policies, technical-docs, financial-reports, legal-contracts, product-catalog, release-notes-tickets

        Reply ONLY with a JSON object of this shape:
          { "silos": ["silo-name", ...], "confidence": 0.0-1.0, "reasoning": "one short sentence" }

        Query: {0}
        """;

    public LlmClassifierRouter(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
    }

    public async Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var prompt = Prompt.Replace("{0}", query, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var raw = (response.Text ?? "{}").Trim();
        var json = ExtractJson(raw);
        try
        {
            var parsed = JsonSerializer.Deserialize<SemanticDecision>(json) ?? new SemanticDecision();
            // E2: drop any silo the model invented; only the canonical six are routable.
            var validSilos = (parsed.Silos ?? Array.Empty<string>())
                .Where(KnownSilos.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (validSilos.Length == 0)
            {
                // The model returned nothing usable (empty or all-hallucinated).
                return new RoutingDecision(
                    Silos: Array.Empty<string>(),
                    Confidence: 0,
                    Reasoning: "no valid silos",
                    Strategy: Strategy,
                    Escalated: true);
            }
            return new RoutingDecision(
                Silos: validSilos,
                Confidence: parsed.Confidence,
                Reasoning: parsed.Reasoning ?? string.Empty,
                Strategy: Strategy,
                Escalated: true);
        }
        catch (JsonException)
        {
            // The call was still made and still billed, so it counts.
            return new RoutingDecision(Array.Empty<string>(), 0,
                "failed to parse LLM response", Strategy, Escalated: true);
        }
    }

    private static string ExtractJson(string raw)
    {
        var s = raw.IndexOf('{', StringComparison.Ordinal);
        var e = raw.LastIndexOf('}');
        return s < 0 || e < s ? "{}" : raw[s..(e + 1)];
    }

    private sealed record SemanticDecision
    {
        [JsonPropertyName("silos")] public string[]? Silos { get; init; }
        [JsonPropertyName("confidence")] public double Confidence { get; init; }
        [JsonPropertyName("reasoning")] public string? Reasoning { get; init; }
    }
}

/// <summary>
/// Embedding-based semantic router. Holds a small set of exemplar phrases per
/// silo, embeds them once (lazily), and routes a query to the silo(s) whose
/// exemplars are most similar to the query embedding.
/// </summary>
/// <remarks>
/// Unlike <see cref="LlmClassifierRouter"/>, this router never calls a chat
/// model: routing is a single query embedding plus cosine-similarity maths, so
/// it is markedly cheaper and lower-latency. It is deterministic given a
/// deterministic <see cref="IEmbeddingService"/>. Exemplar phrases mirror the
/// themes of <see cref="RuleBasedRouter"/>'s keyword tables.
/// </remarks>
public sealed class SemanticRouter : IQueryRouter
{
    private readonly IEmbeddingService _embeddings;
    private readonly double _threshold;
    private readonly FrozenDictionary<string, string[]> _exemplars;
    private readonly Lazy<Task<FrozenDictionary<string, ReadOnlyMemory<float>[]>>> _exemplarVectors;

    public string Strategy => "semantic-embedding";

    /// <summary>
    /// Default per-silo exemplar phrases. Three to five short phrases drawn from
    /// each silo's domain, paralleling <see cref="RuleBasedRouter"/>'s keyword
    /// themes. Embedded once and cached.
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> DefaultExemplars =
        new Dictionary<string, string[]>
        {
            ["hr-policies"] = ["vacation and leave policy", "parental leave", "remote work policy", "sick days and probation"],
            ["technical-docs"] = ["api endpoint documentation", "service deployment runbook", "architecture decision record", "rate limit configuration"],
            ["financial-reports"] = ["quarterly revenue report", "annual budget and forecast", "fiscal year operating margin", "Q3 financial results"],
            ["legal-contracts"] = ["non-disclosure agreement", "contract termination clause", "statement of work", "software license agreement"],
            ["product-catalog"] = ["pricing tiers and features", "enterprise plan specification", "starter and team plans", "product feature comparison"],
            ["release-notes-tickets"] = ["latest release notes", "support ticket and hotfix", "regression in a release", "bug fix changelog"],
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Create the embedding router.</summary>
    /// <param name="embeddings">The embedding service used to vectorize the query and exemplars.</param>
    /// <param name="similarityThreshold">
    /// Cosine-similarity floor (default 0.35). Silos whose best exemplar clears
    /// this floor are selected; if none clear it, the single top silo is returned.
    /// </param>
    /// <param name="exemplars">
    /// Optional override of the per-silo exemplar phrases. Defaults to a built-in
    /// set mirroring the rule-based keyword themes.
    /// </param>
    public SemanticRouter(
        IEmbeddingService embeddings,
        double similarityThreshold = 0.35,
        IReadOnlyDictionary<string, string[]>? exemplars = null)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        _embeddings = embeddings;
        _threshold = similarityThreshold;
        _exemplars = exemplars is null
            ? DefaultExemplars
            : exemplars.ToFrozenDictionary(StringComparer.Ordinal);
        // Lazy<Task<...>> embeds every exemplar exactly once, on first RouteAsync,
        // and caches the resulting vectors. Concurrent first callers await the
        // same task rather than re-embedding.
        _exemplarVectors = new Lazy<Task<FrozenDictionary<string, ReadOnlyMemory<float>[]>>>(
            EmbedExemplarsAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var exemplarVectors = await _exemplarVectors.Value.ConfigureAwait(false);
        var queryVector = await _embeddings.EmbedQueryAsync(query, cancellationToken).ConfigureAwait(false);

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (silo, vectors) in exemplarVectors)
        {
            var best = 0.0;
            foreach (var vector in vectors)
            {
                best = Math.Max(best, CosineKernel.Cosine(queryVector.Span, vector.Span));
            }
            scores[silo] = best;
        }

        if (scores.Count == 0)
        {
            return new RoutingDecision(Array.Empty<string>(), 0, "no exemplars configured", Strategy);
        }

        var (topSilo, topScore) = scores.OrderByDescending(kv => kv.Value).First();
        var selected = scores.Where(kv => kv.Value >= _threshold).Select(kv => kv.Key).ToArray();
        if (selected.Length == 0)
        {
            // Nothing cleared the threshold — fall back to the single best silo so
            // the query is still routed somewhere rather than dropped.
            selected = [topSilo];
        }

        var confidence = Math.Clamp(topScore, 0.0, 1.0);
        var reasoning = FormattableString.Invariant($"top silo {topSilo} at similarity {topScore:F3}");
        return new RoutingDecision(
            Silos: selected,
            Confidence: confidence,
            Reasoning: reasoning,
            Strategy: Strategy,
            Escalated: true);
    }

    private async Task<FrozenDictionary<string, ReadOnlyMemory<float>[]>> EmbedExemplarsAsync()
    {
        var result = new Dictionary<string, ReadOnlyMemory<float>[]>(StringComparer.Ordinal);
        foreach (var (silo, phrases) in _exemplars)
        {
            var vectors = new ReadOnlyMemory<float>[phrases.Length];
            for (var i = 0; i < phrases.Length; i++)
            {
                vectors[i] = await _embeddings.EmbedQueryAsync(phrases[i]).ConfigureAwait(false);
            }
            result[silo] = vectors;
        }
        return result.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

/// <summary>
/// Multi-source router — runs the rule-based router first, falls back to the
/// secondary router (the LLM-classifier or embedding router) if rule-based
/// confidence is below <see cref="ConfidenceThreshold"/> (default 0.5). Returns
/// the union of silos when both routers contribute.
/// </summary>
public sealed class MultiSourceRouter : IQueryRouter
{
    private readonly IQueryRouter _ruleBased;
    private readonly IQueryRouter _llmFallback;
    public double ConfidenceThreshold { get; }

    public MultiSourceRouter(IQueryRouter ruleBased, IQueryRouter llmFallback, double confidenceThreshold = 0.5)
    {
        ArgumentNullException.ThrowIfNull(ruleBased);
        ArgumentNullException.ThrowIfNull(llmFallback);
        _ruleBased = ruleBased;
        _llmFallback = llmFallback;
        ConfidenceThreshold = confidenceThreshold;
    }

    public string Strategy => $"multi({_ruleBased.Strategy}+{_llmFallback.Strategy})";

    public async Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        var ruleDecision = await _ruleBased.RouteAsync(query, cancellationToken).ConfigureAwait(false);
        if (ruleDecision.Confidence >= ConfidenceThreshold)
        {
            // Answered by cheap rules alone -- no LLM, no embedding. Escalated
            // stays false even though Strategy names the composite.
            return ruleDecision with { Strategy = Strategy, Escalated = false };
        }
        var fallback = await _llmFallback.RouteAsync(query, cancellationToken).ConfigureAwait(false);
        var union = ruleDecision.Silos.Union(fallback.Silos, StringComparer.Ordinal).ToArray();
        return new RoutingDecision(
            Silos: union,
            Confidence: Math.Max(ruleDecision.Confidence, fallback.Confidence),
            Reasoning: $"rule={ruleDecision.Reasoning}; fallback={fallback.Reasoning}",
            Strategy: Strategy,
            Escalated: true);
    }
}
