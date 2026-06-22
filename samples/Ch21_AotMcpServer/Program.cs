// Chapter 21 — §2.9 Native AOT MCP server (minimal variant).
//
// A deliberately small, self-contained MCP server over stdio whose only
// dependency is the MCP SDK + the generic host. It is the AOT TARGET the
// chapter discusses: `<PublishAot>true</PublishAot>` in the csproj, a
// System.Text.Json source-generated context so serialization needs no
// reflection, and a single tool registered by its concrete type
// (`WithTools<DocsSearchTool>()`) rather than by assembly scanning — the
// scanning overload is the trim-hostile one.
//
// Why a minimal variant and not the full SmartDocs pipeline: the production
// pipeline pulls in Qdrant (gRPC), the Azure SDK, and Npgsql, which are not
// AOT-annotated and would bury the example in third-party trim warnings. The
// honest, buildable demonstration is this lean server. See README.md for the
// exact `dotnet publish` command and which assemblies still emit AOT warnings.
//
// stdio rule: stdout is the JSON-RPC wire — all logging goes to stderr.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagInDotNet.Samples.Ch21_AotMcpServer;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<DocsSearchTool>(AotJsonContext.Default.Options);

await builder.Build().RunAsync().ConfigureAwait(false);
