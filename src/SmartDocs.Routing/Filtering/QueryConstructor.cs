using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using SmartDocs.Core.Filtering;

namespace SmartDocs.Routing.Filtering;

/// <summary>The structured filter we ask the LLM to extract.</summary>
public sealed record ExtractedFilter
{
    [JsonPropertyName("department")] public string? Department { get; init; }
    [JsonPropertyName("office")] public string? Office { get; init; }
    [JsonPropertyName("silo")] public string? Silo { get; init; }
    [JsonPropertyName("documentType")] public string? DocumentType { get; init; }
    [JsonPropertyName("fiscalYear")] public int? FiscalYear { get; init; }
    [JsonPropertyName("freeTextQuery")] public string? FreeTextQuery { get; init; }
}

/// <summary>
/// Translates a natural-language query into a structured
/// <see cref="ExtractedFilter"/> via an <see cref="IChatClient"/>. The model
/// is instructed to reply with strict JSON; we deserialize and the caller
/// composes the resulting filter into the retrieval call.
/// </summary>
public sealed class QueryConstructor
{
    private readonly IChatClient _chat;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public QueryConstructor(IChatClient chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
    }

    private const string Prompt =
        """
        Extract structured filter parameters from the user's question. Return ONLY a
        JSON object that matches this exact schema; omit fields that are not implied
        by the question.

        {
          "department":  "HR" | "Engineering" | "Finance" | "Legal" | "Product" | null,
          "office":      "Montreal" | "Paris" | "Casablanca" | null,
          "silo":        "hr-policies" | "technical-docs" | "financial-reports" | "legal-contracts" | "product-catalog" | "release-notes-tickets" | null,
          "documentType": "Policy" | "ADR" | "Runbook" | "Reference" | "Report" | "Contract" | "Specification" | "ReleaseNote" | "Ticket" | null,
          "fiscalYear":  2024 | 2025 | 2026 | null,
          "freeTextQuery": "the question stripped of structured constraints"
        }

        Question: {0}
        """;

    public async Task<ExtractedFilter> ExtractAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var prompt = Prompt.Replace("{0}", question, StringComparison.Ordinal);
        var response = await _chat.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
        var raw = (response.Text ?? "{}").Trim();
        if (raw.IndexOf('{', StringComparison.Ordinal) < 0)
        {
            // No JSON object at all -> free-text-only fallback.
            return new ExtractedFilter { FreeTextQuery = question };
        }
        var json = ExtractJsonObject(raw);
        try
        {
            return JsonSerializer.Deserialize<ExtractedFilter>(json, JsonOptions) ?? new ExtractedFilter();
        }
        catch (JsonException)
        {
            return new ExtractedFilter { FreeTextQuery = question };
        }
    }

    /// <summary>Pull the first {...} block from a possibly-markdown-fenced LLM response.</summary>
    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{', StringComparison.Ordinal);
        var end = raw.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return "{}";
        }
        return raw[start..(end + 1)];
    }

    /// <summary>Convert an extracted filter into a runnable <see cref="MetadataFilter"/>.</summary>
    public static MetadataFilter ToMetadataFilter(ExtractedFilter extracted)
    {
        ArgumentNullException.ThrowIfNull(extracted);
        var filter = MetadataFilter.All;
        if (!string.IsNullOrWhiteSpace(extracted.Department))
        {
            var v = extracted.Department;
            filter = filter.And(MetadataFilter.Where(m => m.Department == v));
        }
        if (!string.IsNullOrWhiteSpace(extracted.Office))
        {
            var v = extracted.Office;
            filter = filter.And(MetadataFilter.Where(m => m.Office == v));
        }
        if (!string.IsNullOrWhiteSpace(extracted.Silo))
        {
            var v = extracted.Silo;
            filter = filter.And(MetadataFilter.Where(m => m.Silo == v));
        }
        if (!string.IsNullOrWhiteSpace(extracted.DocumentType))
        {
            var v = extracted.DocumentType;
            filter = filter.And(MetadataFilter.Where(m => m.DocumentType == v));
        }
        if (extracted.FiscalYear is { } year)
        {
            filter = filter.And(MetadataFilter.Where(m => m.FiscalYear == year));
        }
        return filter;
    }
}
