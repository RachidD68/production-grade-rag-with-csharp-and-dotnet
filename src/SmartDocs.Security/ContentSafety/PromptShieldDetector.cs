using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security.ContentSafety;

/// <summary>
/// Azure AI <b>Prompt Shields</b>-backed <see cref="IInjectionDetector"/>, implemented
/// as a thin typed <see cref="HttpClient"/> client over the REST contract.
/// <para>
/// <b>Why REST and not the SDK:</b> Prompt Shields is GA, but in .NET it is
/// REST-only. No shipped <c>Azure.AI.ContentSafety</c> NuGet (only
/// <c>1.0.0-beta.1</c> and <c>1.0.0</c> exist, both pinned to service api-version
/// <c>2023-10-01</c>, predating Prompt Shields) exposes a <c>ShieldPrompt</c> /
/// <c>AnalyzePromptShieldAsync</c> method. So this detector calls the documented
/// route directly:
/// </para>
/// <code>
/// POST {endpoint}/contentsafety/text:shieldPrompt?api-version=2024-09-01
/// Content-Type: application/json
/// { "userPrompt": "...", "documents": ["...", "..."] }
///
/// → { "userPromptAnalysis": { "attackDetected": true },
///     "documentsAnalysis": [ { "attackDetected": false } ] }
/// </code>
/// <para>
/// <b>Live endpoint + auth required.</b> This path needs a live Content Safety
/// resource. The library stays credential-agnostic: the caller configures the
/// injected <see cref="HttpClient"/> with the endpoint
/// (<see cref="HttpClient.BaseAddress"/>) and an auth header — either an Entra
/// bearer token (<c>Authorization: Bearer &lt;token&gt;</c>, e.g. from
/// <c>DefaultAzureCredential</c>/<c>ManagedIdentityCredential</c>) or a resource
/// key (<c>Ocp-Apim-Subscription-Key: &lt;key&gt;</c>). This assembly therefore
/// never references <c>Azure.Identity</c>/<c>Azure.Core</c>. It is build-verified
/// and unit-tested offline against a stub handler; it is not exercised against the
/// live service in CI.
/// </para>
/// </summary>
public sealed class PromptShieldDetector : IInjectionDetector
{
    private const string DefaultApiVersion = "2024-09-01";

    private readonly HttpClient _httpClient;
    private readonly string _apiVersion;

    /// <summary>
    /// Creates a detector over a pre-configured <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">
    /// An <see cref="HttpClient"/> whose <see cref="HttpClient.BaseAddress"/> is set
    /// to the Content Safety endpoint and whose default request headers carry the
    /// auth (bearer token or <c>Ocp-Apim-Subscription-Key</c>). The client is owned
    /// by the caller (typically an <c>IHttpClientFactory</c> typed client).
    /// </param>
    /// <param name="apiVersion">
    /// Content Safety service api-version. Defaults to <c>2024-09-01</c>, the first
    /// version that ships the Prompt Shields route.
    /// </param>
    public PromptShieldDetector(HttpClient httpClient, string apiVersion = DefaultApiVersion)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);
        _httpClient = httpClient;
        _apiVersion = apiVersion;
    }

    /// <inheritdoc />
    public async Task<InjectionAnalysis> AnalyzeAsync(
        string userPrompt,
        IReadOnlyList<string>? documents = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);

        var request = new ShieldPromptRequest(userPrompt, documents ?? []);

        using var response = await _httpClient
            .PostAsJsonAsync(
                $"contentsafety/text:shieldPrompt?api-version={_apiVersion}",
                request,
                PromptShieldJsonContext.Default.ShieldPromptRequest,
                cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync(
                PromptShieldJsonContext.Default.ShieldPromptResult,
                cancellationToken)
            .ConfigureAwait(false)
            ?? new ShieldPromptResult(null, null);

        var findings = new List<string>();
        var detected = false;

        if (result.UserPromptAnalysis?.AttackDetected == true)
        {
            detected = true;
            findings.Add("prompt-shields:user-prompt-attack");
        }

        if (result.DocumentsAnalysis is { Count: > 0 } documentsAnalysis)
        {
            for (var i = 0; i < documentsAnalysis.Count; i++)
            {
                if (documentsAnalysis[i]?.AttackDetected == true)
                {
                    detected = true;
                    findings.Add($"prompt-shields:document-attack[{i}]");
                }
            }
        }

        // Prompt Shields returns a boolean verdict, not a graded score, so we
        // surface a binary confidence: 1.0 on any detected attack, 0.0 when clean.
        var score = detected ? 1.0 : 0.0;
        return new InjectionAnalysis(detected, score, findings);
    }
}

/// <summary>Request body for <c>POST text:shieldPrompt</c>.</summary>
internal sealed record ShieldPromptRequest(
    [property: JsonPropertyName("userPrompt")] string UserPrompt,
    [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents);

/// <summary>Response body from <c>POST text:shieldPrompt</c>.</summary>
internal sealed record ShieldPromptResult(
    [property: JsonPropertyName("userPromptAnalysis")] PromptShieldAttackAnalysis? UserPromptAnalysis,
    [property: JsonPropertyName("documentsAnalysis")] IReadOnlyList<PromptShieldAttackAnalysis>? DocumentsAnalysis);

/// <summary>Per-target attack verdict in a Prompt Shields response.</summary>
internal sealed record PromptShieldAttackAnalysis(
    [property: JsonPropertyName("attackDetected")] bool AttackDetected);

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for the Prompt Shields
/// request/response DTOs (no reflection-based serialization at run time).
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ShieldPromptRequest))]
[JsonSerializable(typeof(ShieldPromptResult))]
internal sealed partial class PromptShieldJsonContext : JsonSerializerContext;
