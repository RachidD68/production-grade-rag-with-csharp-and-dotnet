# 0002 — Synthetic dataset carries full Ch 11 metadata schema from day 1

Date: 2026-05-02

## Status

Accepted

## Context

The dataset generator produces synthetic Markdown documents in Phase 0, well before Chapter 11 (Metadata Filtering and Query Construction) introduces structured metadata. We had two options:

1. **Minimal front-matter now, augment in Phase 3.** Ship Phase 0 documents with just `id` and `silo`, and rewrite the generator + regenerate the 300 documents in Phase 3 once the Ch 11 schema is finalized.
2. **Full Ch 11 schema now.** Ship every document with `department`, `office`, `confidentialityLevel`, `documentType`, `fiscalYear`, `author`, and `lastModified` — even though no consumer of those fields exists yet.

## Decision

Ship the **full Ch 11 metadata schema** in every Phase 0 document.

## Consequences

**Positive**:
- The dataset is stable for the entire build. Phases 1–10 can read it without ever needing to regenerate. CI gates that verify dataset SHA256s are deterministic — a regeneration in Phase 3 would force every downstream eval gate to re-baseline.
- Earlier phases (e.g., Ch 6 vector DB ingestion) can already exercise metadata-on-vector capabilities without "schema migration" boilerplate.
- The synthetic content is tied to the metadata (e.g., HR policies are about a specific office, financial reports have an actual fiscal year), so the metadata is *real* signal — not just decoration.

**Negative**:
- Phase 0 generator is more complex than strictly required.
- If the Ch 11 schema changes during the book's writing (e.g., a new field is needed), we eat one regeneration cost. We accept that risk because the schema is well-grounded in the spec doc.

**Neutral**:
- Front-matter increases file size by ~150 bytes/doc → ~45 KB extra over the corpus. Negligible.
