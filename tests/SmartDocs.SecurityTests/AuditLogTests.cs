using SmartDocs.Security.Audit;

namespace SmartDocs.SecurityTests;

public sealed class AuditLogTests
{
    /// <summary>In-memory <see cref="IAuditSink"/> that keeps every appended line.</summary>
    private sealed class MemorySink : IAuditSink
    {
        public List<string> Lines { get; } = [];

        public Task AppendAsync(string line, CancellationToken cancellationToken = default)
        {
            Lines.Add(line);
            return Task.CompletedTask;
        }
    }

    private static AuditRecord Record(string query, string response)
        => new(DateTimeOffset.UnixEpoch, query, response, UserId: "user-1");

    [Fact]
    public async Task Three_appended_records_form_a_valid_chain()
    {
        var sink = new MemorySink();
        var log = new AuditLog(sink, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        await log.AppendAsync(Record("q1", "a1"), CancellationToken.None);
        await log.AppendAsync(Record("q2", "a2"), CancellationToken.None);
        await log.AppendAsync(Record("q3", "a3"), CancellationToken.None);

        var entries = sink.Lines.Select(Deserialize).ToList();
        Assert.Equal(3, entries.Count);
        Assert.True(AuditLog.VerifyChain(entries));
    }

    [Fact]
    public async Task Tampering_with_a_record_breaks_the_chain()
    {
        var sink = new MemorySink();
        var log = new AuditLog(sink, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        await log.AppendAsync(Record("q1", "a1"), CancellationToken.None);
        await log.AppendAsync(Record("q2", "a2"), CancellationToken.None);
        await log.AppendAsync(Record("q3", "a3"), CancellationToken.None);

        var entries = sink.Lines.Select(Deserialize).ToList();

        // Forge the middle record's response while leaving its stored hash intact.
        var forged = entries[1] with { Record = entries[1].Record with { Response = "TAMPERED" } };
        entries[1] = forged;

        Assert.False(AuditLog.VerifyChain(entries));
    }

    [Fact]
    public async Task Checkpoint_seals_head_and_extends_the_chain()
    {
        var sink = new MemorySink();
        var log = new AuditLog(sink, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        await log.AppendAsync(Record("q1", "a1"), CancellationToken.None);
        await log.CheckpointAsync(CancellationToken.None);

        var entries = sink.Lines.Select(Deserialize).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("checkpoint", entries[1].Record.Kind);
        Assert.True(AuditLog.VerifyChain(entries));
    }

    private static AuditChainEntry Deserialize(string line)
        => System.Text.Json.JsonSerializer.Deserialize<AuditChainEntry>(line)!;
}
