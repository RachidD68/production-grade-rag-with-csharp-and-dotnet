// Chapter 18 — Model Context Protocol — Streamable HTTP MCP server (prod).
//
// The production sibling of the stdio server (samples/Ch18_McpServer): the same
// SmartDocs pipeline and the same tools, exposed over the Streamable HTTP
// transport at a single /mcp endpoint, behind JWT bearer authentication.
//
// Key differences from the stdio host:
//   * Authentication — JWT bearer (OAuth 2.0 Resource-Server model). The MCP
//     endpoint requires an authenticated principal: MapMcp().RequireAuthorization().
//   * Tenant scope — derived per request from the ClaimsPrincipal
//     (ClaimsTenantContext), not a fixed dev default.
//   * Transport — WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).
//     Stateless is the spec default since MCP 2026-07-28 (no Mcp-Session-Id, no
//     standalone GET stream). Switch to Stateful only when the server must push
//     list-changed / resource-updated notifications over a subscriptions/listen
//     stream; StatefulForInitializeClients keeps a session only for clients that
//     still speak the pre-2026 initialize handshake.
//
// MapMcp() is the line the chapter's listing omits — without it there is no
// /mcp endpoint to route requests to.
//
// Run:
//   dotnet run --project samples/Ch18_McpHttpServer
// Configure Mcp:Authority / Mcp:Audience (appsettings or env) to point at your
// OAuth authorization server.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol.AspNetCore;
using RagInDotNet.Samples.Ch18_McpHttpServer;
using RagInDotNet.Samples.Ch18_McpServer; // shared offline pipeline (linked)
using SmartDocs.Mcp;

var builder = WebApplication.CreateBuilder(args);

// The same offline SmartDocs pipeline the stdio host uses (dev/prod parity).
builder.Services.AddSmartDocsOfflinePipeline();

// Per-request tenant scope, derived from the authenticated principal.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, ClaimsTenantContext>();

// JWT bearer: the MCP server acts as an OAuth 2.0 Resource Server. Authority +
// Audience point at the authorization server that issues the access tokens.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Mcp:Authority"];
        o.Audience = builder.Configuration["Mcp:Audience"];
    });
builder.Services.AddAuthorization();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless) // Stateful only for subscriptions/listen
    .WithToolsFromAssembly(typeof(SearchTool).Assembly)
    .WithResources<ChunkResource>();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Single /mcp endpoint, gated behind authentication. MapMcp() is the line the
// chapter's listing omits; the explicit "/mcp" pattern pins the conventional
// endpoint path (the default is the app root).
app.MapMcp("/mcp").RequireAuthorization();

await app.RunAsync().ConfigureAwait(false);

/// <summary>
/// Exposed so the integration smoke test can host this server in-process with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
