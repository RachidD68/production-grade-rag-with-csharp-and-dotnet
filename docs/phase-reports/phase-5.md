# Phase 5 — Design Patterns (Ch 15–19)

**Status**: ✅ complete
**Test count delta**: +15 (95 → 110 unit)

## What was built

| Ch | Code |
|---|---|
| 15 | `SmartDocs.Retrieval/Decorators/`: `HydeRetriever`, `RagFusionRetriever`, `CragRetriever` (with optional WebFallback) |
| 16 | `SmartDocs.Retrieval/Vectorless/`: `StructuralIndex` / `StructuralNode`, `DocumentStructureParser` (Markdown headings → tree), `VectorlessRetriever` (LLM picks section indices), `StructuralVectorRetriever` (vectorless first, vector fallback) |
| 17 | `SmartDocs.Retrieval/Graph/`: `GraphRagPipeline` (eager — extract + connected-component communities + per-community summary); `LazyGraphRagRetriever` (defers all summarisation to query time per the Microsoft Research paper) |
| 18 | `SmartDocs.Mcp/`: `[McpServerToolType]` `SmartDocsMcpTools` exposing `Search` / `GetChunk` / `Ingest`. Configured via static `Configure(retriever)` at host startup. Wire via `builder.Services.AddMcpServer().WithToolsFromAssembly()` in any host that wants to expose retrieval to MCP clients (Claude Desktop, GitHub Copilot, MAF agents via `HostedMcpTool`). |
| 19 | `SmartDocs.Agents/`: `SmartDocsAgent.Create()` builds a `ChatClientAgent` with vector / graph / web `AIFunction` tools; `MultiAgentWorkflow` (Researcher → Analyst → FactChecker → Writer with loop-back on NOT_SUPPORTED). Sequential implementation; production projects can swap to the official `Microsoft.Agents.AI.Workflows` graph builder. |

## Conscious simplifications

- Ch 17 community detection uses connected components, not Leiden. Production MS GraphRAG pipelines should call into the Python implementation via the JSON-exchange adapter (the spec's "Going Deeper" path).
- Ch 19 multi-agent workflow is sequential, not graph-driven through `Microsoft.Agents.AI.Workflows.WorkflowBuilder`. Wiring to that builder is documented in the chapter narrative as the production target; Phase 8 polish adds it if the chapter author needs the live demo.
- A2A handoff demo (`Microsoft.Agents.AI.A2A.AspNetCore`) deferred — package is preview-only.
- Mem0 / Letta agentic memory adapter not shipped — the chapter narrative discusses both as REST-driven; Phase 8 polish adds a Letta REST client if needed for the book's case study.
