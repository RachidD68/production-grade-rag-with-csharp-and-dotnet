# Infra

Infrastructure-as-code for **Contoso SmartDocs**.

## Local development

There is no local container stack. Every service the book uses installs
**natively** on your machine, and the default app + tests need only **Ollama** —
everything else runs against the built-in in-memory vector store and cache.

➡️ See **[`../docs/local-setup.md`](../docs/local-setup.md)** for per-service
native install steps (Ollama, PostgreSQL + pgvector, Qdrant, Neo4j, Redis), the
ports the code expects, and the `RUN_*_INTEGRATION` test gates.

## Azure (production)

[`azure/`](azure/) holds the Bicep templates for the production deployment
(Ch 25 capstone). See [`azure/README.md`](azure/README.md).
