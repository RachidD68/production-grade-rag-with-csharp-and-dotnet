using System.Collections.Concurrent;
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SmartDocs.Mcp;

/// <summary>
/// A queued ingest request awaiting processing by the (out-of-band) ingestion
/// pipeline. The stub queue records these so a caller can poll a tracking id;
/// wiring the real pipeline is left to the host.
/// </summary>
/// <param name="TrackingId">Opaque id the caller polls for status.</param>
/// <param name="Silo">The logical silo the content is queued for.</param>
/// <param name="Status">Queue status (always <c>queued</c> in the stub).</param>
public sealed record IngestTicket(string TrackingId, string Silo, string Status);

/// <summary>
/// Seam for the write side of the corpus. Kept deliberately small: the
/// <c>ingest</c> tool depends on this rather than a concrete pipeline so the
/// host can swap the stub for a real ingestion service without touching the
/// tool. Implementations must be safe to call from per-request tool instances.
/// </summary>
public interface IIngestSink
{
    /// <summary>Queue content for ingestion and return its tracking ticket.</summary>
    Task<IngestTicket> EnqueueAsync(string content, string silo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IIngestSink"/> — an in-memory stub that records tickets
/// and returns a tracking id without doing any real work. Register as a
/// singleton so queued tickets survive across the per-request tool instances.
/// </summary>
public sealed class InMemoryIngestSink : IIngestSink
{
    private readonly ConcurrentQueue<IngestTicket> _queue = new();

    /// <summary>The tickets queued so far, exposed for tests and diagnostics.</summary>
    public IReadOnlyCollection<IngestTicket> Queued => _queue.ToArray();

    /// <inheritdoc />
    public Task<IngestTicket> EnqueueAsync(string content, string silo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(silo);
        cancellationToken.ThrowIfCancellationRequested();

        var trackingId = Guid.NewGuid().ToString("N")[..12];
        var ticket = new IngestTicket(trackingId, silo, "queued");
        _queue.Enqueue(ticket);
        return Task.FromResult(ticket);
    }
}

/// <summary>
/// MCP <em>write</em> tool. Unlike every other tool in this server, <c>ingest</c>
/// mutates the corpus, so it widens the security surface: a write tool lets a
/// client add content that later searches will surface, which is a privilege
/// no read tool grants. It is flagged accordingly (<c>ReadOnly = false</c>,
/// <c>Destructive = false</c>, <c>OpenWorld = true</c>) and its description says
/// so explicitly, so a host can gate, audit, or omit it independently of the
/// read tools. The queue here is a stub (<see cref="InMemoryIngestSink"/>) — it
/// records a tracking id and returns; the real ingestion pipeline is wired by
/// the host.
/// </summary>
[McpServerToolType]
public sealed class IngestTool
{
    private readonly IIngestSink _queue;

    /// <summary>Create the tool over the (stub) ingest queue.</summary>
    public IngestTool(IIngestSink queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
    }

    /// <summary>Queue free-text content for ingestion into a silo. WRITE operation — see the type remarks.</summary>
    [McpServerTool(Name = "ingest", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true),
     Description(
        "WRITE TOOL — mutates the corpus and widens the security surface. Queues free-text " +
        "content for ingestion into a silo and returns a tracking id. Unlike the read-only " +
        "search tools, enabling this lets a client add content that later searches will " +
        "surface; gate, audit, or omit it accordingly.")]
    public async Task<IngestTicket> IngestAsync(
        [Description("Free-text content to ingest.")] string content,
        [Description("Logical silo (e.g. hr-policies, technical-docs).")] string silo,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(silo);
        return await _queue.EnqueueAsync(content, silo, cancellationToken).ConfigureAwait(false);
    }
}
