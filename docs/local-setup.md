# Local setup (native install, no Docker)

Everything in this repo runs against services you install **natively on your
machine** — no Docker, no containers, no Azure subscription required to get
started. This guide lists what to install, in what order, and which chapters
actually need each service.

The golden rule: **start only what the chapter you're reading needs.** The
default app and every test run against an in-memory vector store, so for most of
the book the single required install is **Ollama**.

> Windows is the reference platform (PowerShell commands below). macOS/Linux
> equivalents are noted where they differ; the ports and connection strings are
> identical.

---

## What each service is for

| Service | Default port(s) | Required? | Used by |
|---|---|---|---|
| **.NET 10 SDK** | — | **Yes** | everything |
| **Ollama** | 11434 | **Yes** (default LLM + embeddings) | Ch 1, 3, 9, 21 + every sample's default provider |
| **PostgreSQL + pgvector** | 5432 | Optional (recommended persistent store) | Ch 14 hybrid retrieval (`PostgresHybridRetriever`) |
| **Qdrant** | 6333 (REST), 6334 (gRPC) | Optional (alternate vector store) | Ch 6, 8, 14, 17, 24 |
| **Neo4j** | 7474 (HTTP), 7687 (Bolt) | Optional (graph chapters) | Ch 13, 14, 17, 24 |
| **Redis** | 6379 | Optional (distributed cache) | Ch 21, 24 |

**By default nothing but Ollama is needed.** The API (`SmartDocs.Api`), the
Hello-World sample, and the whole test suite use the built-in
`InMemoryVectorStore` and an in-memory cache. Persistent stores (Postgres,
Qdrant, Neo4j, Redis) are opt-in — a chapter wires one only when it supplies a
connection string via `UseQdrant(...)`, `UseNeo4j(...)`, `EnableCaching(...)`, or
`HybridRetrieverOptions.PostgresConnectionString`.

---

## 1. .NET 10 SDK (required)

Install from <https://dot.net> (or `winget install Microsoft.DotNet.SDK.10`).
Verify:

```powershell
dotnet --version   # 10.0.x
```

Then, from the repo root:

```powershell
dotnet restore
dotnet build
```

---

## 2. Ollama (required — local LLM + embeddings)

Ollama is the default provider (see [ADR-0008](decisions/0008-default-provider-ollama.md)):
no API key, no cloud, no spend.

1. Download and run the installer from <https://ollama.com/download> —
   `OllamaSetup.exe` on Windows (`brew install ollama` on macOS,
   `curl -fsSL https://ollama.com/install.sh | sh` on Linux). It installs a
   background service that listens on **http://localhost:11434**.
2. Pull the two models the book uses by default:

   ```powershell
   ollama pull nomic-embed-text   # 768-dim embeddings (~270 MB)
   ollama pull llama3.2           # chat model (~2 GB)
   ```

3. Verify:

   ```powershell
   ollama list                                   # both models listed
   curl http://localhost:11434/api/tags          # JSON with the two models
   ```

That's enough to run the API and the Hello-World sample end to end.

> **Switching to Azure OpenAI** is a config-only change (no install) — see
> [ADR-0008](decisions/0008-default-provider-ollama.md) for the
> `SmartDocs:Llm:Provider = AzureOpenAI` settings.

---

## 3. PostgreSQL + pgvector (optional — recommended persistent vector store)

Used by the Ch 14 hybrid retriever (`PostgresHybridRetriever`, dense pgvector +
sparse `tsvector` fused with RRF in SQL). This is the recommended store when you
want persistence without a cloud account.

1. **PostgreSQL 16/17** — install the EDB Windows installer from
   <https://www.postgresql.org/download/windows/> (`winget install
   PostgreSQL.PostgreSQL.17`). It runs as a Windows service on **5432**.
   `tsvector` full-text search is built in — nothing extra for the sparse leg.
2. **pgvector** — the vector extension isn't bundled. On Windows, build it once
   against your PostgreSQL install with the MSVC toolchain (per the official
   instructions at <https://github.com/pgvector/pgvector#windows>), or skip the
   build by pointing the connection string at a **managed Postgres** that ships
   pgvector as a click-to-enable extension (Azure Database for PostgreSQL
   Flexible Server, Supabase, Neon). On macOS/Linux, `brew install pgvector` or
   your package manager.
3. Create the database and enable the extension:

   ```sql
   CREATE DATABASE smartdocs;
   \c smartdocs
   CREATE EXTENSION IF NOT EXISTS vector;
   ```

4. Wire it up with a standard Npgsql connection string, e.g.
   `Host=localhost;Port=5432;Database=smartdocs;Username=postgres;Password=...`.

---

## 4. Qdrant (optional — alternate vector store)

An alternate to pgvector, used by the Ch 6/8/17/24 samples.

1. Download the Windows release binary from
   <https://github.com/qdrant/qdrant/releases> (or the platform build for
   macOS/Linux) and run `qdrant.exe`. It listens on **6333** (REST) and **6334**
   (gRPC) with an on-disk `./storage` folder.
2. Verify: `curl http://localhost:6333/healthz` and open the dashboard at
   <http://localhost:6333/dashboard>.
3. Wire it up with `AddSmartDocsQdrantVectorStore(...)` (gRPC host `localhost`,
   port `6334`) or `UseQdrant("http://localhost:6334")`.

Or use **Qdrant Cloud** and point the connection string at the managed endpoint.

---

## 5. Neo4j (optional — graph chapters)

Used by the Ch 13/17 graph and GraphRAG retrievers.

1. Install **Neo4j Community 5.26** from <https://neo4j.com/download-center/>
   (needs a JDK 17/21). Unzip, then drop the **APOC** plugin jar into `plugins/`
   and allow `apoc.*` in `conf/neo4j.conf`
   (`dbms.security.procedures.unrestricted=apoc.*`).
2. Set the initial password and start it:

   ```powershell
   bin\neo4j-admin dbms set-initial-password <your-password>
   bin\neo4j console
   ```

   Bolt listens on **7687**, the browser on <http://localhost:7474>.
3. Wire it up with `UseNeo4j("bolt://localhost:7687")` and your credentials.

Or use a free **Neo4j AuraDB** instance and point the Bolt URI at it — no local
install.

---

## 6. Redis (optional — distributed cache)

Ch 21 demonstrates a distributed response cache. **You don't need Redis to run
Ch 21** — omit `EnableCaching(...)` and the pipeline uses the in-memory cache it
ships with.

If you want the real thing on Windows, install **Memurai** (a native
Windows-service Redis-compatible server, <https://www.memurai.com>) or run
`redis-server` under **WSL2**. Either listens on **6379**. macOS/Linux:
`brew install redis` / your package manager. Wire it up with
`EnableCaching("localhost:6379")`.

---

## Integration tests

The integration tests hit locally-installed services on their default ports and
are **gated behind environment variables** so they're skipped unless you opt in
and the service is running:

| Env var | Service | Port |
|---|---|---|
| `RUN_OLLAMA_INTEGRATION=1` | Ollama | 11434 |
| `RUN_QDRANT_INTEGRATION=1` | Qdrant | 6334 |
| `RUN_AZURE_SEARCH_INTEGRATION=1` | Azure AI Search | (cloud) |

```powershell
# Example: run the real-Ollama integration tests
$env:RUN_OLLAMA_INTEGRATION = "1"
dotnet test --filter "FullyQualifiedName~Ollama"
```

Unit and security tests need no services and run with a plain `dotnet test`.

---

## Observability (optional)

The book's OpenTelemetry wiring (Ch 21) exports to any OTLP endpoint but is
**off by default** — the code runs without a collector. If you want a local
dashboard, point the OTLP exporter at any OTLP-compatible receiver
(`OTEL_EXPORTER_OTLP_ENDPOINT`); none is required to run the samples.
