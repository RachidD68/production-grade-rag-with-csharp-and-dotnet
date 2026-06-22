// Chapter 18 — Model Context Protocol — stdio MCP server (dev).
//
// A runnable, OFFLINE Model Context Protocol server that exposes the SmartDocs
// retrieval pipeline as MCP tools and resources over the stdio transport — the
// transport Claude Desktop, VS Code, and Cursor use to launch a local server.
//
// Tools (constructor-DI instance classes, discovered via WithToolsFromAssembly):
//   search             top-K vector search (read-only)
//   search_and_rerank  retrieve-broadly, rerank-precisely (read-only, default)
//   graph_search       LazyGraphRAG subgraph summaries (read-only)
//   get_chunk          by-id chunk lookup, clearance-scoped (read-only)
//   ingest             WRITE tool — queues content; widens the security surface
// Resource template:
//   smartdocs://chunk/{id}   chunk text by id, clearance-scoped
//
// Tenant scoping is Option A: a FixedTenantContext (full-access dev default)
// here; the HTTP host derives it from the authenticated principal instead.
//
// CRITICAL (stdio): stdout is the JSON-RPC wire. ALL logging goes to stderr —
// a single stray Console.WriteLine on stdout corrupts the protocol.
//
// Run (the host blocks on stdin — that is expected for a stdio server):
//   dotnet run --project samples/Ch18_McpServer
// See README.md for the Claude Desktop / VS Code MCP config block.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagInDotNet.Samples.Ch18_McpServer;
using SmartDocs.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel on stdio: route every log line to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// The offline SmartDocs pipeline the tools depend on (embedder, store, dense
// retriever, no-op reranker, graph retriever, chunk lookup, ingest sink).
builder.Services.AddSmartDocsOfflinePipeline();

// stdio has no authenticated principal — use the full-access dev tenant.
builder.Services.AddSingleton<ITenantContext>(new FixedTenantContext());

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(SearchTool).Assembly)
    .WithResources<ChunkResource>();

await builder.Build().RunAsync().ConfigureAwait(false);
