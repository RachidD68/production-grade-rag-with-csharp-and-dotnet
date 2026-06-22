# SmartDocs.Mcp

Model Context Protocol (MCP) server surface for the **SmartDocs** RAG stack — the
companion library to *Production-Grade RAG with C# and .NET*.

Exposes the SmartDocs retrieval pipeline to MCP clients (IDEs, agents) through tools
(`search`, `get_chunk`, `ingest`, `graph_search`) and chunk resources, built on the
official `ModelContextProtocol` SDK. The surface is kept trim/AOT-clean so it can be
published ahead-of-time as a self-contained MCP server.

## Install

```bash
dotnet add package SmartDocs.Mcp
```

## License

MIT © Rachid Dahir
