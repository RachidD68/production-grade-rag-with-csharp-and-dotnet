using System.Text.Json;
using SmartDocs.Core.Documents;

namespace SmartDocs.Generation.Citations;

/// <summary>One row of the EU AI Act / GDPR audit log.</summary>
public sealed record AuditEntry(
    DateTimeOffset TimestampUtc,
    string Query,
    IReadOnlyList<string> RetrievedChunkIds,
    string Response,
    IReadOnlyList<Citation> Citations,
    double FaithfulnessScore,
    string? UserId);

/// <summary>
/// EU AI Act-aware audit logger. Captures the full request shape — query,
/// retrieved chunks, response, citations, faithfulness score, timestamp,
/// optional user id — for every grounded answer the system produces. The
/// retention sink is injected (file, Azure Monitor, Cosmos DB, etc.) so
/// the same logger can run in dev and in regulated production.
/// </summary>
public sealed class AuditLogger
{
    private readonly Func<AuditEntry, Task> _sink;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public AuditLogger(Func<AuditEntry, Task> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    public Task LogAsync(
        string query,
        IReadOnlyList<RetrievalResult> retrievedSources,
        string response,
        IReadOnlyList<Citation> citations,
        double faithfulnessScore,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(retrievedSources);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(citations);

        var entry = new AuditEntry(
            TimestampUtc: DateTimeOffset.UtcNow,
            Query: query,
            RetrievedChunkIds: [.. retrievedSources.Select(s => s.Chunk.ChunkId)],
            Response: response,
            Citations: citations,
            FaithfulnessScore: faithfulnessScore,
            UserId: userId);
        return _sink(entry);
    }

    /// <summary>Convenience: append-only JSONL file sink.</summary>
    public static Func<AuditEntry, Task> JsonlFileSink(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var lockObj = new Lock();
        return entry =>
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (lockObj)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
            return Task.CompletedTask;
        };
    }
}
