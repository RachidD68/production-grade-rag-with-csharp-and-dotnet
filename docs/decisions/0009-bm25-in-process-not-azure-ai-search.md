# 0009 — In-process BM25 for the Sparse retriever (not Azure AI Search) in Phase 2

Date: 2026-05-02

## Status

Accepted (Phase 2). Will be re-evaluated in Phase 7 (Ch 25 capstone).

## Context

The mission brief and spec both describe Ch 8's `SparseRetriever` as "BM25 keyword search via Azure AI Search". Azure AI Search ships a strong, scalable BM25 implementation as part of its hybrid search surface. However it requires:

- An Azure subscription
- A provisioned Azure AI Search resource
- An API key
- Network connectivity from the test runner

For the Phase 2 deliverable to be runnable on a laptop with no Azure account, those preconditions are unacceptable.

## Decision

Phase 2 ships an in-process BM25 implementation in `SmartDocs.Retrieval/SparseRetriever.cs`. The math is the standard Robertson / Spärck-Jones / Okapi formulation (default `k1=1.5`, `b=0.75`). The retriever exposes an explicit `Index(IEnumerable<DocumentChunk>)` method that the caller invokes to build the inverted index from the corpus.

The interface (`IRetriever`) is unchanged, so swapping in an `AzureAISearchSparseRetriever` later is a one-line DI substitution. Phase 7 (Ch 25) will add the Azure AI Search adapter as the production-default sparse path.

## Consequences

**Positive**:
- Tests run offline.
- The reader can study a real BM25 implementation in C# (~80 lines) instead of treating the score as a black box.
- The `HybridRetriever` round-trip is exercised end-to-end without external services.

**Negative**:
- The in-process implementation is single-process — it does not scale to millions of documents. The chapter narrative makes this explicit.
- BM25 parameters (`k1`, `b`) are not tunable per term as Azure AI Search allows.

**Neutral**:
- Tokenization is a simple Unicode word regex (`[\\p{L}\\d_]+` with lowercase folding); language-aware analyzers (stemming, stop words) live in Azure AI Search and aren't reproduced here.
