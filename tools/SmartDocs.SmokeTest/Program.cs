// SmartDocs.SmokeTest — post-deploy smoke test (Ch 25).
//
// A runnable console that verifies a *deployed* SmartDocs service satisfies its
// public HTTP contract. Run it as a deploy gate (the chapter's deploy-prod.yml
// invokes it against the freshly rolled-out service) and it exits non-zero on
// the first failed assertion so the pipeline halts before promoting a bad build.
//
// Contract checked:
//   1. GET  /health                 → 2xx
//   2. POST /api/ask/stream {q}      → SSE frames: a "sources" event, a "done"
//                                      event, and total "token" Data length > 0
//   3. GET  /admin/metrics          → 2xx
//
// Framework-only: HttpClient + System.Text.Json + the shared-framework
// System.Net.ServerSentEvents.SseParser. No external packages.
//
// Usage:  smoke-test --baseurl http://localhost:8080 [--question "..."]

using System.Net.Http.Json;
using System.Net.ServerSentEvents;

var baseUrl = GetArg(args, "--baseurl") ?? "http://localhost:8080";
var question = GetArg(args, "--question") ?? "How many vacation days do I get?";

if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
{
    Console.Error.WriteLine($"FAIL  --baseurl is not a valid absolute URL: '{baseUrl}'");
    return 2;
}

Console.WriteLine($"SmartDocs smoke test → {baseUri}");

using var http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(100) };

var failures = 0;
try
{
    await CheckHealthAsync(http).ConfigureAwait(false);
}
catch (Exception ex)
{
    Fail($"/health threw: {ex.Message}");
}

try
{
    await CheckStreamAsync(http, question).ConfigureAwait(false);
}
catch (Exception ex)
{
    Fail($"/api/ask/stream threw: {ex.Message}");
}

try
{
    await CheckMetricsAsync(http).ConfigureAwait(false);
}
catch (Exception ex)
{
    Fail($"/admin/metrics threw: {ex.Message}");
}

if (failures == 0)
{
    Console.WriteLine("PASS  all smoke checks succeeded.");
    return 0;
}

Console.Error.WriteLine($"FAIL  {failures} smoke check(s) failed.");
return 1;

// ── Checks ───────────────────────────────────────────────────────────────────

async Task CheckHealthAsync(HttpClient client)
{
    using var response = await client.GetAsync("/health").ConfigureAwait(false);
    if (response.IsSuccessStatusCode)
    {
        Pass($"GET /health → {(int)response.StatusCode}");
    }
    else
    {
        Fail($"GET /health → {(int)response.StatusCode} (expected 2xx)");
    }
}

async Task CheckStreamAsync(HttpClient client, string q)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask/stream")
    {
        Content = JsonContent.Create(new { question = q }),
    };
    request.Headers.Accept.ParseAdd("text/event-stream");

    using var response = await client
        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
        .ConfigureAwait(false);

    if (!response.IsSuccessStatusCode)
    {
        Fail($"POST /api/ask/stream → {(int)response.StatusCode} (expected 2xx)");
        return;
    }

    var sawSources = false;
    var sawDone = false;
    var totalTokenLength = 0;

    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
    var parser = SseParser.Create(stream);
    await foreach (var item in parser.EnumerateAsync().ConfigureAwait(false))
    {
        switch (item.EventType)
        {
            case "sources":
                sawSources = true;
                break;
            case "token":
                totalTokenLength += item.Data?.Length ?? 0;
                break;
            case "done":
                sawDone = true;
                break;
            default:
                break;
        }
    }

    AssertTrue(sawSources, "SSE stream contained a 'sources' event");
    AssertTrue(sawDone, "SSE stream contained a 'done' event");
    AssertTrue(totalTokenLength > 0, $"SSE 'token' frames carried data (total length = {totalTokenLength})");
}

async Task CheckMetricsAsync(HttpClient client)
{
    using var response = await client.GetAsync("/admin/metrics").ConfigureAwait(false);
    if (response.IsSuccessStatusCode)
    {
        Pass($"GET /admin/metrics → {(int)response.StatusCode}");
    }
    else
    {
        Fail($"GET /admin/metrics → {(int)response.StatusCode} (expected 2xx)");
    }
}

// ── Assertion / reporting helpers ────────────────────────────────────────────

void AssertTrue(bool condition, string description)
{
    if (condition)
    {
        Pass(description);
    }
    else
    {
        Fail(description);
    }
}

void Pass(string message) => Console.WriteLine($"PASS  {message}");

void Fail(string message)
{
    failures++;
    Console.Error.WriteLine($"FAIL  {message}");
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
