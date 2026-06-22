using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security;

/// <summary>
/// Fully offline default <see cref="IInjectionDetector"/>. Reuses the
/// <see cref="InputSanitizer"/> regex telltales (and the
/// <see cref="ContentFilter"/> credential/system-prompt-echo signals) to score
/// the user prompt and every retrieved document for prompt-injection attempts.
/// <para>
/// A "hard" finding — direct prompt injection, a jailbreak, or a system-prompt
/// extraction attempt — sets <see cref="InjectionAnalysis.Detected"/> to true
/// with <see cref="InjectionAnalysis.Score"/> 1.0. Cosmetic findings (invisible
/// characters stripped, length truncation) are reported but do not, on their
/// own, flag the input. This is the detector used in CI and offline samples;
/// swap in <c>PromptShieldDetector</c> for an Azure-hosted signal.
/// </para>
/// </summary>
public sealed class HeuristicInjectionDetector : IInjectionDetector
{
    private static readonly string[] HardFindings =
    [
        "direct-prompt-injection",
        "jailbreak-attempt",
        "system-prompt-extraction",
    ];

    private readonly InputSanitizer _sanitizer;

    public HeuristicInjectionDetector(InputSanitizer? sanitizer = null)
        => _sanitizer = sanitizer ?? new InputSanitizer();

    public Task<InjectionAnalysis> AnalyzeAsync(
        string userPrompt,
        IReadOnlyList<string>? documents = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<string>();
        var hardHit = false;

        hardHit |= Inspect(userPrompt, "prompt", findings);

        if (documents is not null)
        {
            for (var i = 0; i < documents.Count; i++)
            {
                hardHit |= Inspect(documents[i], $"document[{i}]", findings);
            }
        }

        var analysis = new InjectionAnalysis(
            Detected: hardHit,
            Score: hardHit ? 1.0 : 0.0,
            Findings: findings);
        return Task.FromResult(analysis);
    }

    private bool Inspect(string text, string source, List<string> findings)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var sanitized = _sanitizer.Sanitize(text);
        var hardHit = false;
        foreach (var f in sanitized.Findings)
        {
            findings.Add($"{source}:{f}");
            if (Array.IndexOf(HardFindings, f) >= 0)
            {
                hardHit = true;
            }
        }

        // ContentFilter catches a planted document that echoes a system prompt
        // ("You are a helpful assistant…") — a common indirect-injection shape.
        var filtered = ContentFilter.Inspect(text);
        foreach (var f in filtered.Findings)
        {
            findings.Add($"{source}:{f}");
            if (string.Equals(f, "system-prompt-echo", StringComparison.Ordinal))
            {
                hardHit = true;
            }
        }

        return hardHit;
    }
}
