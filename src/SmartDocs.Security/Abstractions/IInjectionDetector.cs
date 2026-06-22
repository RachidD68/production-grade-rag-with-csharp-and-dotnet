namespace SmartDocs.Security.Abstractions;

/// <summary>
/// Verdict from an <see cref="IInjectionDetector"/>.
/// </summary>
/// <param name="Detected">True when the analyzed text is judged to contain a prompt-injection attempt.</param>
/// <param name="Score">Confidence in [0, 1]; 1.0 is a certain hit, 0.0 is clean.</param>
/// <param name="Findings">Human-readable telltales explaining the verdict.</param>
public sealed record InjectionAnalysis(bool Detected, double Score, IReadOnlyList<string> Findings);

/// <summary>
/// Detects prompt-injection attempts in a user prompt and, optionally, in the
/// retrieved documents that will be concatenated into the model context
/// (indirect / cross-prompt injection). Implementations range from a fully
/// offline heuristic (<c>HeuristicInjectionDetector</c>) to an Azure-hosted
/// content-safety service (<c>PromptShieldDetector</c>).
/// </summary>
public interface IInjectionDetector
{
    /// <summary>
    /// Analyzes <paramref name="userPrompt"/> and each entry of
    /// <paramref name="documents"/> for injection telltales.
    /// </summary>
    Task<InjectionAnalysis> AnalyzeAsync(
        string userPrompt,
        IReadOnlyList<string>? documents = null,
        CancellationToken cancellationToken = default);
}
