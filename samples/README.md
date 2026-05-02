# Samples — Per-Chapter Micro-Projects

Each folder is a self-contained, runnable demo for the chapter named in the folder. Run any of them with:

```bash
dotnet run --project samples/<folder>
```

| Folder | Chapter | What it shows |
|---|---|---|
| `Ch01_HelloWorldRag` | 1 | 80-line single-file RAG: in-memory list, brute-force cosine, `[Source N]` prompt, grounded answer. The "dopamine hit". |
| `Ch02_SkToMafMigration` | 2 | One canonical Semantic Kernel pattern translated to MAF: `ChatClientAgent` + `AIFunctionFactory.Create` + `AgentSession`. SK "before" lives as a documentation comment. |
| `Ch03_EmbeddingBenchmarks` | 3 | Embeds 50 HR passages + 10 similar / 10 dissimilar pairs against one or more local Ollama models; reports dim, mean/p50/p95 latency, cosine quality margin. |
| `Ch04_ChunkingPlayground` | 4 | Side-by-side chunk-count + avg-length + first-5-chunks preview for FixedSize, Sentence, Recursive, and CodeFile (Roslyn) chunkers. |
| `Ch25_VerticalSliceVariant` | 25 | The Vertical Slice "Going Deeper" alternative to the layered `SmartDocs.Api`. Each feature owns its own request/response/handler/registration in one file. |

## Deferred (Phase 8 polish slot)

These were listed in the spec but not yet shipped:

- `Ch04_ContextualRetrievalDemo` — reproduces Anthropic's 49% / 67% retrieval-failure reduction on the legal silo. The `ContextualChunker` is in place; the demo pipeline (eval-driven recall comparison) is the missing piece.
- `Ch06_VectorDbComparison` — runs 200 chunks through InMemory + Qdrant + Azure AI Search and prints p50/p95 + recall@10. The two adapters that are implemented (InMemory + Qdrant) make this near-free; only the Azure AI Search adapter blocks it.
- `Ch07_IndexingStrategies` — the 4-strategy comparison on a single 20-page ADR.
- `Ch16_VectorlessRetrieval` — Contoso-legal-silo demo of structural vs vector retrieval.
- `Ch17_LazyGraphRagDemo` — reproduces the indexing-cost reduction on the legal silo.
- `Ch18_McpServer` — packaged MCP server runnable from Claude Desktop via stdio.
- `Ch19_MultiAgentOrchestration` — stripped-down 3-agent orchestration showcasing the `Microsoft.Agents.AI.Workflows` graph builder explicitly.
- `Ch22_DriftAdapterMigration` — the small → large model migration with measurable quality recovery.

Each is an evening's work given the underlying primitives are all shipped. Pick them up alongside the chapter writing.
