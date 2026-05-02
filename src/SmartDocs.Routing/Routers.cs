using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

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
/// novel phrasings.
/// </summary>
public sealed class SemanticRouter : IQueryRouter
{
    private readonly IChatClient _chat;
    public string Strategy => "semantic";

    private const string Prompt =
        """
        Classify the following query into one or more of these silos:
          hr-policies, technical-docs, financial-reports, legal-contracts, product-catalog, release-notes-tickets

        Reply ONLY with a JSON object of this shape:
          { "silos": ["silo-name", ...], "confidence": 0.0-1.0, "reasoning": "one short sentence" }

        Query: {0}
        """;

    public SemanticRouter(IChatClient chat)
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
            return new RoutingDecision(
                Silos: parsed.Silos ?? Array.Empty<string>(),
                Confidence: parsed.Confidence,
                Reasoning: parsed.Reasoning ?? string.Empty,
                Strategy: Strategy);
        }
        catch (JsonException)
        {
            return new RoutingDecision(Array.Empty<string>(), 0,
                "failed to parse LLM response", Strategy);
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
/// Multi-source router — runs the rule-based router first, falls back to
/// the semantic router if rule-based confidence is below
/// <see cref="ConfidenceThreshold"/> (default 0.5). Returns the union of
/// silos when both routers agree.
/// </summary>
public sealed class MultiSourceRouter : IQueryRouter
{
    private readonly IQueryRouter _ruleBased;
    private readonly IQueryRouter _semantic;
    public double ConfidenceThreshold { get; }

    public MultiSourceRouter(IQueryRouter ruleBased, IQueryRouter semantic, double confidenceThreshold = 0.5)
    {
        ArgumentNullException.ThrowIfNull(ruleBased);
        ArgumentNullException.ThrowIfNull(semantic);
        _ruleBased = ruleBased;
        _semantic = semantic;
        ConfidenceThreshold = confidenceThreshold;
    }

    public string Strategy => $"multi({_ruleBased.Strategy}+{_semantic.Strategy})";

    public async Task<RoutingDecision> RouteAsync(string query, CancellationToken cancellationToken = default)
    {
        var ruleDecision = await _ruleBased.RouteAsync(query, cancellationToken).ConfigureAwait(false);
        if (ruleDecision.Confidence >= ConfidenceThreshold)
        {
            return ruleDecision with { Strategy = Strategy };
        }
        var semantic = await _semantic.RouteAsync(query, cancellationToken).ConfigureAwait(false);
        var union = ruleDecision.Silos.Union(semantic.Silos, StringComparer.Ordinal).ToArray();
        return new RoutingDecision(
            Silos: union,
            Confidence: Math.Max(ruleDecision.Confidence, semantic.Confidence),
            Reasoning: $"rule={ruleDecision.Reasoning}; semantic={semantic.Reasoning}",
            Strategy: Strategy);
    }
}
