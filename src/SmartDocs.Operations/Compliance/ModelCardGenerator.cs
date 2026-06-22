using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SmartDocs.Operations.Compliance;

/// <summary>A live model/version snapshot row on the model card.</summary>
/// <param name="Role">What the model does in the pipeline (e.g. <c>chat</c>, <c>embedding</c>, <c>reranker</c>).</param>
/// <param name="Name">The deployed model name.</param>
/// <param name="Version">The pinned model version / deployment tag.</param>
public sealed record ModelEntry(string Role, string Name, string Version);

/// <summary>One corpus / data-source row on the model card.</summary>
/// <param name="Silo">The corpus silo (e.g. <c>hr-policies</c>).</param>
/// <param name="DocumentCount">How many documents the silo currently holds.</param>
/// <param name="ConfidentialityLevel">The highest confidentiality level present in the silo.</param>
public sealed record DataSourceEntry(string Silo, int DocumentCount, string ConfidentialityLevel);

/// <summary>A headline evaluation metric carried onto the model card.</summary>
public sealed record MetricEntry(string Name, double Value);

/// <summary>
/// The structured model card. Rendered to markdown by an
/// <see cref="IModelCardRenderer"/> and signed with a detached signature so a
/// reader can prove the published card matches what the system actually ran.
/// </summary>
public sealed record ModelCard(
    string SystemName,
    string Version,
    DateTimeOffset GeneratedAt,
    string IntendedUse,
    IReadOnlyList<ModelEntry> Models,
    IReadOnlyList<DataSourceEntry> DataSources,
    IReadOnlyList<MetricEntry> PerformanceMetrics,
    IReadOnlyList<string> KnownFailureModes,
    IReadOnlyList<string> OperationalControls);

/// <summary>Live snapshot of the deployed models. Production reads the deployment registry / config.</summary>
public interface IModelRegistry
{
    Task<IReadOnlyList<ModelEntry>> GetModelsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Live inventory of the corpus. Production aggregates over the document store.</summary>
public interface ICorpusInventory
{
    Task<IReadOnlyList<DataSourceEntry>> GetDataSourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Renders a <see cref="ModelCard"/> to a publishable string (markdown by default).</summary>
public interface IModelCardRenderer
{
    string Render(ModelCard card);
}

/// <summary>Default markdown renderer for a <see cref="ModelCard"/>.</summary>
public sealed class MarkdownModelCardRenderer : IModelCardRenderer
{
    public string Render(ModelCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var sb = new StringBuilder();
        sb.Append("# Model Card: ").Append(card.SystemName).Append(" v").Append(card.Version).Append('\n');
        sb.Append("\n_Generated ")
            .Append(card.GeneratedAt.ToString("u", CultureInfo.InvariantCulture))
            .Append("_\n");

        sb.Append("\n## Intended Use\n\n").Append(card.IntendedUse).Append('\n');

        sb.Append("\n## Models\n\n");
        sb.Append("| Role | Name | Version |\n|---|---|---|\n");
        foreach (var m in card.Models)
        {
            sb.Append("| ").Append(m.Role).Append(" | ").Append(m.Name).Append(" | ").Append(m.Version).Append(" |\n");
        }

        sb.Append("\n## Data Sources\n\n");
        sb.Append("| Silo | Documents | Confidentiality |\n|---|---|---|\n");
        foreach (var d in card.DataSources)
        {
            sb.Append("| ").Append(d.Silo).Append(" | ")
                .Append(d.DocumentCount.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(d.ConfidentialityLevel).Append(" |\n");
        }

        sb.Append("\n## Performance Metrics\n\n");
        sb.Append("| Metric | Value |\n|---|---|\n");
        foreach (var p in card.PerformanceMetrics)
        {
            sb.Append("| ").Append(p.Name).Append(" | ")
                .Append(p.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append(" |\n");
        }

        sb.Append("\n## Known Failure Modes\n\n");
        foreach (var f in card.KnownFailureModes)
        {
            sb.Append("- ").Append(f).Append('\n');
        }

        sb.Append("\n## Operational Controls\n\n");
        foreach (var c in card.OperationalControls)
        {
            sb.Append("- ").Append(c).Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>
/// Assembles and signs a live model card. Pulls the deployed model snapshot
/// (<see cref="IModelRegistry"/>) and corpus inventory (<see cref="ICorpusInventory"/>)
/// at generation time, folds in the supplied eval-baseline metrics, renders the
/// card to markdown (<see cref="IModelCardRenderer"/>), and produces a detached
/// signature over the rendered bytes. The signature is an HMAC-SHA256 / RSA tag
/// computed by an injected delegate — the chapter's "signed PDF" is satisfied by
/// "markdown + detached signature"; turning the markdown into a PDF is a
/// downstream doc-pipeline concern and intentionally not done here.
/// </summary>
public sealed class ModelCardGenerator
{
    private readonly IModelRegistry _models;
    private readonly ICorpusInventory _corpus;
    private readonly IModelCardRenderer _renderer;
    private readonly Func<byte[], string> _sign;
    private readonly ModelCardProfile _profile;
    private readonly TimeProvider _timeProvider;

    public ModelCardGenerator(
        IModelRegistry models,
        ICorpusInventory corpus,
        IModelCardRenderer renderer,
        Func<byte[], string> sign,
        ModelCardProfile profile,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(sign);
        ArgumentNullException.ThrowIfNull(profile);
        _models = models;
        _corpus = corpus;
        _renderer = renderer;
        _sign = sign;
        _profile = profile;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Convenience factory for the common HMAC-SHA256 signer over the rendered
    /// bytes (lowercase hex), mirroring the Ch 23 provenance-signer convention.
    /// </summary>
    public static Func<byte[], string> HmacSigner(ReadOnlyMemory<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Signing key must be non-empty.", nameof(key));
        }
        var keyBytes = key.ToArray();
        return bytes => Convert.ToHexStringLower(HMACSHA256.HashData(keyBytes, bytes));
    }

    /// <summary>Build the structured <see cref="ModelCard"/> from the live snapshots.</summary>
    public async Task<ModelCard> BuildAsync(CancellationToken cancellationToken = default)
    {
        var models = await _models.GetModelsAsync(cancellationToken).ConfigureAwait(false);
        var dataSources = await _corpus.GetDataSourcesAsync(cancellationToken).ConfigureAwait(false);

        return new ModelCard(
            SystemName: _profile.SystemName,
            Version: _profile.Version,
            GeneratedAt: _timeProvider.GetUtcNow(),
            IntendedUse: _profile.IntendedUse,
            Models: models,
            DataSources: dataSources,
            PerformanceMetrics: _profile.PerformanceMetrics,
            KnownFailureModes: _profile.KnownFailureModes,
            OperationalControls: _profile.OperationalControls);
    }

    /// <summary>Build, render, and sign the card. Returns the markdown and a detached signature.</summary>
    public async Task<(string Markdown, string Signature)> GenerateSignedAsync(CancellationToken cancellationToken = default)
    {
        var card = await BuildAsync(cancellationToken).ConfigureAwait(false);
        var markdown = _renderer.Render(card);
        var signature = _sign(Encoding.UTF8.GetBytes(markdown));
        return (markdown, signature);
    }

    /// <summary>Build, render, and return just the rendered markdown.</summary>
    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var (markdown, _) = await GenerateSignedAsync(cancellationToken).ConfigureAwait(false);
        return markdown;
    }
}

/// <summary>
/// The static, human-authored half of a model card: the parts that are policy
/// rather than live telemetry (intended use, known failure modes, operational
/// controls, headline eval metrics from the latest baseline run).
/// </summary>
public sealed record ModelCardProfile(
    string SystemName,
    string Version,
    string IntendedUse,
    IReadOnlyList<MetricEntry> PerformanceMetrics,
    IReadOnlyList<string> KnownFailureModes,
    IReadOnlyList<string> OperationalControls);
