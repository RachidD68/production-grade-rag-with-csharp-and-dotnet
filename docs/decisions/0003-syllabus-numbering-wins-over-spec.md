# 0003 — Syllabus numbering wins over the project specification

Date: 2026-05-02

## Status

Accepted

## Context

Two authoritative documents describe the book's structure:

- `RAG_in_DotNet_Syllabus.docx` — **25 chapters across 7 parts** (current revision).
- `Mastering_RAG_Project_Specifications.md` — **24 chapters across 8 parts** (pre-revision).

Where they conflict, chapter numbering is the most jarring example:

| Topic | Syllabus (current) | Spec (pre-revision) |
|---|---|---|
| Embeddings | Ch 3 | Ch 4 |
| Chunking & Contextual Retrieval | Ch 4 | Ch 5 |
| Multimodal Content | Ch 5 | Ch 17 |
| Vector DBs | Ch 6 | Ch 6 |
| The Retriever | Ch 8 | Ch 7 |
| Re-ranking | Ch 9 | Ch 10 |
| Indexing Strategies | Ch 7 | Ch 9 |
| Complete Pipeline | Ch 10 | Ch 8 |

Sample folder names in the spec follow the spec's numbering (`samples/Ch04_EmbeddingBenchmarks/`), but readers will be following the **syllabus**.

## Decision

The syllabus is the single source of truth for chapter numbering. Where the syllabus and the spec disagree:

1. Sample-folder names use the **syllabus** chapter (so the spec's `Ch04_EmbeddingBenchmarks/` becomes `Ch03_EmbeddingBenchmarks/`).
2. README, chapter-map, and ADRs use **syllabus** numbers.
3. The spec is reference material for *class names, method signatures, and design intent*. Numerical discrepancies in the spec are silently translated.

The mission brief explicitly directs this: "Use the syllabus for the current numbering, but trust the spec for class names, method signatures, and design intent."

## Consequences

- Readers can copy chapter numbers from the book directly into `dotnet run --project samples/ChXX_*` without translation.
- ADRs and per-phase reports reference syllabus numbers exclusively.
- Future updates to the spec (or a new spec revision) must conform to the syllabus numbering, or supersede this ADR explicitly.
