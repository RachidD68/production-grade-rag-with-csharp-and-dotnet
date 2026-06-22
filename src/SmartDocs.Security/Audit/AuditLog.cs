using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartDocs.Security.Audit;

/// <summary>One logical audit event: who asked what, and what we answered.</summary>
/// <param name="TimestampUtc">When the event occurred.</param>
/// <param name="Query">The user query (or a checkpoint marker).</param>
/// <param name="Response">The grounded answer (or empty for checkpoints).</param>
/// <param name="UserId">Optional principal id.</param>
/// <param name="Kind">Record kind — <c>"query"</c> (default) or <c>"checkpoint"</c>.</param>
public sealed record AuditRecord(
    DateTimeOffset TimestampUtc,
    string Query,
    string Response,
    string? UserId,
    string Kind = "query");

/// <summary>
/// A persisted line of the hash chain: the record, the previous link's hash, and
/// this link's hash. Replaying these through <see cref="AuditLog.VerifyChain"/>
/// proves the log was not edited, reordered, or truncated.
/// </summary>
public sealed record AuditChainEntry(
    AuditRecord Record,
    string PrevHash,
    string Hash);

/// <summary>
/// Append-only destination for serialized audit lines.
/// <para>
/// In production the durable seam is an <b>Azure immutable blob</b>
/// (time-based retention / legal-hold WORM container), so even an operator with
/// write access cannot rewrite history. That binding is intentionally a seam,
/// not a dependency: this library ships only the file sink and the interface,
/// and never references <c>Azure.Storage.Blobs</c>. Implement
/// <see cref="IAuditSink"/> over an immutable container in the hosting app.
/// </para>
/// </summary>
public interface IAuditSink
{
    /// <summary>Appends a single pre-serialized line (no trailing newline).</summary>
    Task AppendAsync(string line, CancellationToken cancellationToken = default);
}

/// <summary>Thread-safe append-only JSONL file sink.</summary>
public sealed class FileAuditSink : IAuditSink
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public FileAuditSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public Task AppendAsync(string line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Tamper-evident, hash-chained, append-only audit log. Each appended record's
/// hash is <c>SHA256(prevHash + canonical-json(record))</c>, so any edit to a
/// record (or any reordering / deletion) breaks every link after it. The chain
/// head is tracked in memory; <see cref="CheckpointAsync"/> seals the current
/// head as a periodic checkpoint record that an external monitor can co-sign or
/// publish (for example to an immutable blob) for non-repudiation.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Genesis hash for an empty chain.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IAuditSink _sink;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private string _head = GenesisHash;

    public AuditLog(IAuditSink sink, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The current chain head hash.</summary>
    public string Head
    {
        get { lock (_gate) { return _head; } }
    }

    /// <summary>
    /// Appends <paramref name="record"/>, persists the
    /// <c>{record, prevHash, hash}</c> line, advances the head, and returns the
    /// new head hash.
    /// </summary>
    public async Task<string> AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        string prevHash;
        string hash;
        string line;
        lock (_gate)
        {
            prevHash = _head;
            hash = ComputeHash(prevHash, record);
            line = JsonSerializer.Serialize(new AuditChainEntry(record, prevHash, hash), CanonicalJson);
            _head = hash;
        }

        await _sink.AppendAsync(line, cancellationToken).ConfigureAwait(false);
        return hash;
    }

    /// <summary>
    /// Seals the current head as a checkpoint record (intended to be called on a
    /// timer, e.g. hourly). The checkpoint is itself an appended, chained record,
    /// so it both anchors and extends the chain.
    /// </summary>
    public Task<string> CheckpointAsync(CancellationToken cancellationToken = default)
    {
        var record = new AuditRecord(
            TimestampUtc: _timeProvider.GetUtcNow(),
            Query: $"checkpoint:{Head}",
            Response: string.Empty,
            UserId: null,
            Kind: "checkpoint");
        return AppendAsync(record, cancellationToken);
    }

    /// <summary>
    /// Recomputes the chain over <paramref name="entries"/> in order and returns
    /// true only when every link's <c>PrevHash</c> matches the running head and
    /// its <c>Hash</c> matches <c>SHA256(prevHash + canonical-json(record))</c>.
    /// </summary>
    public static bool VerifyChain(IEnumerable<AuditChainEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var expectedPrev = GenesisHash;
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                return false;
            }
            if (!string.Equals(entry.PrevHash, expectedPrev, StringComparison.Ordinal))
            {
                return false;
            }
            var recomputed = ComputeHash(entry.PrevHash, entry.Record);
            if (!string.Equals(entry.Hash, recomputed, StringComparison.Ordinal))
            {
                return false;
            }
            expectedPrev = entry.Hash;
        }
        return true;
    }

    private static string ComputeHash(string prevHash, AuditRecord record)
    {
        var canonical = JsonSerializer.Serialize(record, CanonicalJson);
        var bytes = Encoding.UTF8.GetBytes(prevHash + canonical);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
