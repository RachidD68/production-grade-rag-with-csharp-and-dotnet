using SmartDocs.Generation.Citations;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// EU AI Act risk tier for a given interaction. Gates how much explanation the
/// UI must surface: limited-risk shows lightweight chips; high-risk must show the
/// full reasoning panel with a human-review escape hatch.
/// </summary>
public enum RiskClassification
{
    Minimal,
    Limited,
    High,
}

/// <summary>One compact "chip" shown next to a limited-risk answer.</summary>
public sealed record ExplanationChip(string Label, string Value);

/// <summary>
/// The rendered explanation surface for an answer. For limited risk only
/// <see cref="Chips"/> is populated; for high risk the full panel adds the cited
/// chunks, the reasoning chain, the model name/version, and a request-human-review
/// action. This is a data structure, not UI — the front end renders it.
/// </summary>
/// <param name="Risk">The risk tier this panel was built for.</param>
/// <param name="ShowFullPanel"><see langword="true"/> for the high-risk full panel.</param>
/// <param name="Chips">Compact key/value chips (always present).</param>
/// <param name="CitedChunks">Cited chunks (high-risk only; empty otherwise).</param>
/// <param name="ReasoningChain">Ordered reasoning steps (high-risk only; empty otherwise).</param>
/// <param name="ModelName">Model name (high-risk only; null otherwise).</param>
/// <param name="ModelVersion">Model version (high-risk only; null otherwise).</param>
/// <param name="RequestHumanReviewAction">Action key the UI binds the review button to (high-risk only; null otherwise).</param>
public sealed record ExplanationView(
    RiskClassification Risk,
    bool ShowFullPanel,
    IReadOnlyList<ExplanationChip> Chips,
    IReadOnlyList<Citation> CitedChunks,
    IReadOnlyList<string> ReasoningChain,
    string? ModelName,
    string? ModelVersion,
    string? RequestHumanReviewAction);

/// <summary>The inputs a panel is built from.</summary>
public sealed record ExplanationContext(
    string Answer,
    double Confidence,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<string> ReasoningChain,
    string ModelName,
    string ModelVersion);

/// <summary>
/// Classifies an interaction into a <see cref="RiskClassification"/> tier. The
/// panel is gated on this injected classifier so the same answer renders as
/// lightweight chips in a limited-risk context and as the full panel in a
/// high-risk one.
/// </summary>
public interface IRiskClassifier
{
    RiskClassification Classify(ExplanationContext context);
}

/// <summary>Risk classifier that always returns a fixed tier (config-driven default).</summary>
public sealed class FixedRiskClassifier : IRiskClassifier
{
    private readonly RiskClassification _risk;

    public FixedRiskClassifier(RiskClassification risk) => _risk = risk;

    public RiskClassification Classify(ExplanationContext context) => _risk;
}

/// <summary>
/// Builds the explanation surface for an answer, gated on an injected
/// <see cref="IRiskClassifier"/>. Limited/minimal risk yields chips only; high
/// risk yields the full panel (cited chunks + reasoning chain + model
/// name/version + a request-human-review action). Returns a data structure for
/// the UI to render.
/// </summary>
public sealed class ExplanationPanel
{
    /// <summary>The action key the high-risk panel binds its review button to.</summary>
    public const string RequestHumanReviewActionKey = "request-human-review";

    private readonly IRiskClassifier _classifier;

    public ExplanationPanel(IRiskClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    /// <summary>Render the explanation view for <paramref name="context"/>, classifying its risk tier.</summary>
    public ExplanationView Render(ExplanationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var risk = _classifier.Classify(context);

        var chips = new List<ExplanationChip>
        {
            new("Confidence", context.Confidence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
            new("Sources", context.Citations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("Model", context.ModelName),
        };

        if (risk != RiskClassification.High)
        {
            return new ExplanationView(
                Risk: risk,
                ShowFullPanel: false,
                Chips: chips,
                CitedChunks: [],
                ReasoningChain: [],
                ModelName: null,
                ModelVersion: null,
                RequestHumanReviewAction: null);
        }

        return new ExplanationView(
            Risk: risk,
            ShowFullPanel: true,
            Chips: chips,
            CitedChunks: context.Citations,
            ReasoningChain: context.ReasoningChain,
            ModelName: context.ModelName,
            ModelVersion: context.ModelVersion,
            RequestHumanReviewAction: RequestHumanReviewActionKey);
    }
}
