# Ch18 — SmartDocs MCP Server (stdio)

A runnable, **offline** Model Context Protocol server that exposes the SmartDocs
retrieval pipeline as MCP tools and a resource over the **stdio** transport — the
transport Claude Desktop, VS Code, and Cursor use to launch a local server.

Everything is deterministic and key-free: an FNV bag-of-words embedder, an
in-memory vector store seeded from a ~180-chunk HR/policy corpus, a base dense
retriever, a no-op reranker, and a stub LazyGraphRAG retriever.

## Tools and resources

| Name | Kind | Notes |
|------|------|-------|
| `search` | tool (read-only) | Top-K vector search. |
| `search_and_rerank` | tool (read-only) | Retrieve broadly, rerank precisely — the default. |
| `graph_search` | tool (read-only) | LazyGraphRAG subgraph summaries. |
| `get_chunk` | tool (read-only) | By-id chunk lookup, clearance-scoped. |
| `ingest` | tool (**WRITE**) | Queues content; widens the security surface. Flagged `ReadOnly = false`. |
| `smartdocs://chunk/{id}` | resource template | Chunk text by id, clearance-scoped. |

All read results are scoped to the caller's clearance at the server boundary
(Option A — reuses Chapter 11's `SecurityContext`/`MetadataFilter`). The stdio
host uses a full-access dev tenant; the HTTP host (`samples/Ch18_McpHttpServer`)
derives the tenant from the authenticated principal.

> **stdio gotcha:** stdout is the JSON-RPC wire. All logging goes to **stderr**
> (`LogToStandardErrorThreshold = LogLevel.Trace`). A stray `Console.WriteLine`
> on stdout corrupts the protocol.

## Run it

```bash
dotnet run --project samples/Ch18_McpServer
```

The server blocks on stdin — that is expected for a stdio server. A host
(Claude Desktop / VS Code / the MCP Inspector) launches it as a child process.

## Claude Desktop config

Add to `claude_desktop_config.json` (Settings → Developer → Edit Config):

```json
{
  "mcpServers": {
    "smartdocs": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\Dahir\\Documents\\Mastering RAG\\RAG-in-DotNet\\samples\\Ch18_McpServer"
      ]
    }
  }
}
```

## VS Code config

Add to `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "smartdocs": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "samples/Ch18_McpServer"]
    }
  }
}
```

## Consuming it from code

`Client.cs` shows the Microsoft Agent Framework (MAF) client path —
`McpClient.CreateAsync(transport)` → `ListToolsAsync()` → `chatClient.AsAIAgent(...)`
→ `agent.RunAsync(...)`. It is compiled but not run by `Program` (it needs a live
chat client). **Footgun:** an `HttpTransportMode.Sse` client against a
Streamable-HTTP server gets an empty tool list and the agent then hallucinates
tool calls — match the client transport mode to the server.
