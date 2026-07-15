using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security.Output;

/// <summary>
/// <see cref="IOutputRedactor"/> backed by Microsoft <b>Presidio</b> — a Python
/// PII analyzer + anonymizer pair exposed over HTTP. It performs the real
/// two-call Presidio flow: POST the text to the analyzer to get recognized PII
/// spans, then POST text + spans to the anonymizer to get the redacted string.
/// <para>
/// <b>Requires a running Presidio deployment</b> (for example the
/// <c>mcr.microsoft.com/presidio-analyzer</c> and <c>presidio-anonymizer</c>
/// containers, or the Presidio AKS sample). The injected <see cref="HttpClient"/>
/// must have its <see cref="HttpClient.BaseAddress"/> set to the analyzer host;
/// the anonymizer base is supplied separately. This path is <b>not exercised in
/// CI / offline</b> — for offline runs use <see cref="OutputRedactor"/>.
/// </para>
/// </summary>
public sealed class PresidioRedactor : IOutputRedactor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly Uri _analyzerUri;
    private readonly Uri _anonymizerUri;
    private readonly string _language;

    /// <param name="http">Client used for both calls.</param>
    /// <param name="analyzerUri">Absolute URI of the Presidio analyzer <c>/analyze</c> endpoint.</param>
    /// <param name="anonymizerUri">Absolute URI of the Presidio anonymizer <c>/anonymize</c> endpoint.</param>
    /// <param name="language">Analyzer language code (default <c>"en"</c>).</param>
    public PresidioRedactor(HttpClient http, Uri analyzerUri, Uri anonymizerUri, string language = "en")
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(analyzerUri);
        ArgumentNullException.ThrowIfNull(anonymizerUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        _http = http;
        _analyzerUri = analyzerUri;
        _anonymizerUri = anonymizerUri;
        _language = language;
    }

    public async ValueTask<string> RedactAsync(string answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Length == 0)
        {
            return answer;
        }

        // 1) Analyze: recognize PII spans.
        var analyzeRequest = new AnalyzeRequest(answer, _language);
        using var analyzeResponse = await _http
            .PostAsJsonAsync(_analyzerUri, analyzeRequest, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        analyzeResponse.EnsureSuccessStatusCode();

        var spans = await analyzeResponse.Content
            .ReadFromJsonAsync<IReadOnlyList<RecognizerResult>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? [];

        if (spans.Count == 0)
        {
            return answer;
        }

        // 2) Anonymize: replace each recognized span with a typed placeholder.
        var anonymizeRequest = new AnonymizeRequest(answer, spans);
        using var anonymizeResponse = await _http
            .PostAsJsonAsync(_anonymizerUri, anonymizeRequest, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        anonymizeResponse.EnsureSuccessStatusCode();

        var result = await anonymizeResponse.Content
            .ReadFromJsonAsync<AnonymizeResult>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return result?.Text ?? answer;
    }

    // ── Presidio wire contracts (subset). ────────────────────────────────────

    private sealed record AnalyzeRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("language")] string Language);

    private sealed record RecognizerResult(
        [property: JsonPropertyName("entity_type")] string EntityType,
        [property: JsonPropertyName("start")] int Start,
        [property: JsonPropertyName("end")] int End,
        [property: JsonPropertyName("score")] double Score);

    private sealed record AnonymizeRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("analyzer_results")] IReadOnlyList<RecognizerResult> AnalyzerResults);

    private sealed record AnonymizeResult(
        [property: JsonPropertyName("text")] string Text);
}
