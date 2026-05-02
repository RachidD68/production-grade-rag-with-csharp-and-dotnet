# Local Infra

Docker Compose stack for running every service the book needs **on a laptop**, with no Azure subscription required.

## Services

| Service | Image | Ports | Used in |
|---|---|---|---|
| Qdrant | `qdrant/qdrant:latest` | 6333 (REST), 6334 (gRPC) | Ch 6, 8, 14, 17, 24 |
| Neo4j 5.26 Community | `neo4j:5.26-community` | 7474 (HTTP), 7687 (Bolt) | Ch 13, 14, 17, 24 |
| Redis 7 | `redis:7-alpine` | 6379 | Ch 21, 24 |
| Ollama | `ollama/ollama:latest` | 11434 | Ch 1, 3, 9, 21 (local fallback for Azure OpenAI) |
| Aspire Dashboard | `mcr.microsoft.com/dotnet/aspire-dashboard:latest` | 18888 (UI), 18889 / 4317 (OTLP) | Ch 21 |

The `ollama-init` sidecar runs once on first start to pull `nomic-embed-text` (the default embedding model) and `llama3.2` (the default chat model). Subsequent `docker compose up -d` invocations are idempotent — no re-download.

## Quick Start

```bash
# 1. Configure secrets
cp infra/.env.example infra/.env
# Edit infra/.env to change NEO4J_PASSWORD if you like.

# 2. Start the stack
cd infra
docker compose up -d

# 3. Verify
docker compose ps          # all five services should be 'running' / 'healthy'

# 4. URLs
#    Qdrant            http://localhost:6333/dashboard
#    Neo4j Browser     http://localhost:7474   (login: neo4j / <NEO4J_PASSWORD>)
#    Ollama API        http://localhost:11434
#    Aspire Dashboard  http://localhost:18888

# 5. Tear down
docker compose down              # stop, keep data
docker compose down -v           # stop, delete volumes (full reset)
```

## Healthchecks

```bash
# Qdrant
curl -s http://localhost:6333/healthz

# Neo4j
docker exec rag-neo4j cypher-shell -u neo4j -p "$(grep NEO4J_PASSWORD infra/.env | cut -d= -f2)" "RETURN 1"

# Redis
docker exec rag-redis redis-cli ping

# Ollama
curl -s http://localhost:11434/api/tags | jq '.models[].name'
```

## Troubleshooting

- **First `ollama-init` run takes minutes** — model downloads are ~3 GB total (nomic-embed-text ~270 MB + llama3.2 ~2 GB). Tail with `docker logs -f rag-ollama-init`.
- **Neo4j refuses connections immediately after start** — wait 30s for warm-up. The healthcheck enforces this.
- **Port 6333 / 7474 / 11434 / 18888 already in use** — change the host-side port mapping in `docker-compose.yml`.
