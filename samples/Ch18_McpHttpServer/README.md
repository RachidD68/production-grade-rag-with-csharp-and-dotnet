# Ch18 — SmartDocs MCP Server (Streamable HTTP, prod)

The production sibling of [`Ch18_McpServer`](../Ch18_McpServer/README.md): the
same SmartDocs pipeline and the same tools, exposed over the **Streamable HTTP**
transport at a single `/mcp` endpoint, behind **JWT bearer** authentication.

## What differs from the stdio host

- **Authentication** — JWT bearer, the OAuth 2.0 Resource-Server model. The MCP
  endpoint requires an authenticated principal:
  `app.MapMcp().RequireAuthorization()`. `MapMcp()` is the line the chapter's
  listing omits.
- **Tenant scope** — derived per request from the `ClaimsPrincipal`
  (`ClaimsTenantContext` reads `clearance` / `silo` / `office` claims), not a
  fixed dev default. Unauthenticated requests collapse to Public clearance.
- **Transport** — `WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)`.
  Stateless is the spec default since MCP 2026-07-28 (no `initialize` handshake,
  no `Mcp-Session-Id`; clients start with `server/discover`). Switch to
  `Stateful` only for `subscriptions/listen` (resource / list-changed
  notifications) — sampling and elicitation no longer need a session: elicitation
  is now a multi-round-trip request (`input_required`) on the ordinary request
  stream, and sampling is deprecated.

## Run it

```bash
dotnet run --project samples/Ch18_McpHttpServer
```

Set the authorization server coordinates (appsettings or environment):

```json
{
  "Mcp": {
    "Authority": "https://login.example.com/",
    "Audience": "smartdocs-mcp"
  }
}
```

The corpus and pipeline (`Corpus.cs`, `Stubs.cs`, `SmartDocsComposition.cs`) are
linked from the stdio sample so both hosts run the same offline corpus — only the
transport and the tenant source differ.
