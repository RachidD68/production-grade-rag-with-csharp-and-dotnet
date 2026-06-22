// SmartDocs.LoadTest — hand-rolled load generator (Ch 25).
//
// Drives POST /api/ask/stream at a target request rate for N minutes and reports
// latency percentiles (p50/p95/p99), throughput, and error rate. It is built from
// the framework only — HttpClient + System.Threading.Channels + Task + Stopwatch —
// deliberately NOT NBomber or any load-test package, so the chapter can show the
// closed-vs-open-model mechanics in plain .NET.
//
// Model: an open-loop generator. A scheduler enqueues one job per request slot at
// the target RPS into a bounded Channel; a fixed pool of worker tasks
// (--concurrency) drains the channel and issues requests. Decoupling arrival rate
// from worker count means a slow server produces queueing (visible as rising
// latency and, once the channel saturates, dropped slots) rather than silently
// throttling the offered load — the honest way to measure a service under load.
//
// Each request streams the SSE response to completion (so latency reflects the
// full answer, not just the first byte) and is timed end to end.
//
// Usage:
//   load-test --baseurl http://localhost:8080 --rps 50 --minutes 1 --concurrency 500
//
// Defaults: --rps 50, --minutes 1, --concurrency = 10 × rps (open-model headroom).

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Threading.Channels;

var baseUrl = GetArg(args, "--baseurl") ?? "http://localhost:8080";
var rps = GetIntArg(args, "--rps") ?? 50;
var minutes = GetDoubleArg(args, "--minutes") ?? 1.0;
// Open-model default: ~10× the steady-state RPS so a slow server queues rather
// than back-pressures the scheduler (the chapter's guidance).
var concurrency = GetIntArg(args, "--concurrency") ?? Math.Max(1, rps * 10);
var question = GetArg(args, "--question") ?? "How many vacation days do I get?";

if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
{
    Console.Error.WriteLine($"--baseurl is not a valid absolute URL: '{baseUrl}'");
    return 2;
}
if (rps <= 0 || minutes <= 0 || concurrency <= 0)
{
    Console.Error.WriteLine("--rps, --minutes, and --concurrency must all be positive.");
    return 2;
}

var duration = TimeSpan.FromMinutes(minutes);
Console.WriteLine(
    $"Load test → {baseUri}  rps={rps}  duration={duration.TotalSeconds:0}s  concurrency={concurrency}");

// Shared HttpClient with a connection pool wide enough for the worker fan-out.
var handler = new SocketsHttpHandler
{
    MaxConnectionsPerServer = concurrency,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
};
using var http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(100) };

// A bounded channel models the request queue. DropWrite means that when the queue
// is full (the server cannot keep up) we count the slot as a dropped/overload
// error rather than blocking the scheduler — keeping the offered rate honest.
var channel = Channel.CreateBounded<long>(new BoundedChannelOptions(concurrency * 4)
{
    SingleReader = false,
    SingleWriter = true,
    FullMode = BoundedChannelFullMode.DropWrite,
});

var latenciesMs = new System.Collections.Concurrent.ConcurrentBag<double>();
long ok = 0, failed = 0, dropped = 0;

using var runCts = new CancellationTokenSource(duration);

// Workers: drain the channel and issue one streamed request per slot.
var workers = new Task[concurrency];
for (var i = 0; i < concurrency; i++)
{
    workers[i] = Task.Run(async () =>
    {
        var reader = channel.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out _))
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    await IssueAsync(http, question).ConfigureAwait(false);
                    sw.Stop();
                    latenciesMs.Add(sw.Elapsed.TotalMilliseconds);
                    Interlocked.Increment(ref ok);
                }
                catch (Exception)
                {
                    sw.Stop();
                    Interlocked.Increment(ref failed);
                }
            }
        }
    });
}

// Scheduler: enqueue one slot every (1 / rps) seconds until the run window ends.
var scheduler = Task.Run(async () =>
{
    var writer = channel.Writer;
    var interval = TimeSpan.FromSeconds(1.0 / rps);
    var clock = Stopwatch.StartNew();
    long slot = 0;
    while (!runCts.IsCancellationRequested)
    {
        if (!writer.TryWrite(slot))
        {
            Interlocked.Increment(ref dropped);
        }
        slot++;

        var nextDue = interval * slot;
        var delay = nextDue - clock.Elapsed;
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(delay, runCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
    writer.Complete();
});

await scheduler.ConfigureAwait(false);
await Task.WhenAll(workers).ConfigureAwait(false);

// ── Report ───────────────────────────────────────────────────────────────────

var samples = latenciesMs.ToArray();
Array.Sort(samples);

var totalAttempts = ok + failed + dropped;
var wallSeconds = duration.TotalSeconds;
var throughput = wallSeconds > 0 ? ok / wallSeconds : 0;
var errorRate = totalAttempts > 0 ? (double)(failed + dropped) / totalAttempts : 0;

Console.WriteLine();
Console.WriteLine("── Results ──────────────────────────────────────────────");
Console.WriteLine($"  Offered slots   : {totalAttempts}");
Console.WriteLine($"  Successful      : {ok}");
Console.WriteLine($"  Failed          : {failed}");
Console.WriteLine($"  Dropped (queue) : {dropped}");
Console.WriteLine($"  Throughput      : {throughput.ToString("0.0", CultureInfo.InvariantCulture)} req/s");
Console.WriteLine($"  Error rate      : {(errorRate * 100).ToString("0.00", CultureInfo.InvariantCulture)} %");
if (samples.Length > 0)
{
    Console.WriteLine($"  Latency p50     : {Percentile(samples, 50).ToString("0.0", CultureInfo.InvariantCulture)} ms");
    Console.WriteLine($"  Latency p95     : {Percentile(samples, 95).ToString("0.0", CultureInfo.InvariantCulture)} ms");
    Console.WriteLine($"  Latency p99     : {Percentile(samples, 99).ToString("0.0", CultureInfo.InvariantCulture)} ms");
    Console.WriteLine($"  Latency max     : {samples[^1].ToString("0.0", CultureInfo.InvariantCulture)} ms");
}
else
{
    Console.WriteLine("  Latency         : no successful samples");
}
Console.WriteLine("─────────────────────────────────────────────────────────");

// A non-zero exit if every request failed makes the tool usable as a coarse gate.
return ok == 0 ? 1 : 0;

// ── Request + math helpers ───────────────────────────────────────────────────

static async Task IssueAsync(HttpClient client, string question)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
    {
        Content = JsonContent.Create(new { question }),
    };
    request.Headers.Accept.ParseAdd("text/event-stream");

    using var response = await client
        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
        .ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    // Drain the SSE body so the measured latency is the full streamed answer.
    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    var buffer = new byte[8192];
    while (await stream.ReadAsync(buffer).ConfigureAwait(false) > 0)
    {
        // discard
    }
}

// Nearest-rank percentile over an ascending-sorted array.
static double Percentile(double[] sortedAscending, double percentile)
{
    if (sortedAscending.Length == 0)
    {
        return 0;
    }
    var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Length);
    var index = Math.Clamp(rank - 1, 0, sortedAscending.Length - 1);
    return sortedAscending[index];
}

static string? GetArg(string[] argv, string name)
{
    for (var i = 0; i < argv.Length - 1; i++)
    {
        if (string.Equals(argv[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return argv[i + 1];
        }
    }
    return null;
}

static int? GetIntArg(string[] argv, string name)
    => int.TryParse(GetArg(argv, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

static double? GetDoubleArg(string[] argv, string name)
    => double.TryParse(GetArg(argv, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
