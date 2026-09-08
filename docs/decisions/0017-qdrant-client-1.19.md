# 0017 — Migrate Qdrant.Client 1.18.1 → 1.19.0 (Universal Query API, storage tiers)

Date: 2026-09-08

## Status

Accepted (follows ADR-0016, which deliberately left this pin alone).

## Context

Qdrant 1.19 (client package `Qdrant.Client` 1.19.0) is the release Chapter 6
describes — TurboQuant as a primary storage mode, per-query sparse IDF, and
the named storage tiers **cold / cached / pinned**. ADR-0016 recorded that
the client bump is *not* a drop-in: under this repository's
warnings-as-errors build, two `[Obsolete]` markings become errors in
`src/SmartDocs.Retrieval/VectorStores/QdrantVectorStore.cs`:

- `ScalarQuantization.AlwaysRam` (CS0612) — the boolean that kept quantized
  vectors in RAM is retired in favour of the `Memory` tier enum
  (`Cold`, `Cached`, `Pinned`), the same model the 1.19 server exposes.
  `VectorParams.OnDisk` is obsoleted the same way (not used here).
- `QdrantClient.SearchAsync(collection, vector, filter, …)` (CS0618, "Use
  QueryAsync instead") — the legacy points/search surface. `QueryAsync` is the
  Universal Query API (Qdrant 1.10+) that `QdrantHybridRetriever` has used since
  Chapter 14 for its prefetch + RRF fusion call.

Both members still function; the errors are deprecation warnings promoted by
the build policy. Staying on 1.18.1 was correct for the 1.20 pass (a pin bump
must not hide a code change); doing the port as its own change is this ADR.

## Decision

- `Qdrant.Client` → **1.19.0** in `Directory.Packages.props`.
- `QdrantVectorStore.BuildVectorParams`: `AlwaysRam = true` →
  `Memory = Memory.Pinned`. Pinned is the tier that matches the previous
  behaviour (always in RAM); the doc comment now explains the three tiers.
- `QdrantVectorStore.SearchAsync` (the `IVectorStore` method — its public
  signature is unchanged) now calls `QdrantClient.QueryAsync` with the query
  vector as a nearest-vector `Query` (implicit conversion from `float[]`), the
  same compiled gRPC `Filter`, `limit`, and `payloadSelector: true`. Result
  handling is unchanged (`ScoredPoint.Payload` / `Score`).
- `QdrantQuantizationTests.Quantization_set_to_int8_scalar_when_enabled`
  asserts `HasMemory` and `Memory == Memory.Pinned` instead of `AlwaysRam`.

## Consequences

- `dotnet build` is 0 warnings / 0 errors with warnings-as-errors; the suite is
  unchanged at **458 tests** (372 unit + 71 security + 15 integration; 1
  skipped, the ONNX real-model test).
- **The live Qdrant round-trip** (`QdrantVectorStoreTests`, gated by
  `RUN_QDRANT_INTEGRATION=1` + a local server on 6334) exercises the new
  `QueryAsync` path and the pinned-tier collection creation; it is the test to
  run against a 1.19 server before relying on this in production. It was not
  executed on the authoring machine for this ADR (no local Qdrant binary
  installed); the port compiles against the 1.19 API and the unit test covers
  the collection-creation shape.
- Behaviour on older servers: `Memory` is a 1.19 server concept. A 1.18 server
  ignores or rejects the tier field depending on its strict-mode settings;
  run the client and server on the same minor, as Chapter 6 already advises.
- Book: Chapter 6's Qdrant 1.19 sidebar and Appendices C and F now state the
  1.19.0 pin; the Chapter 6 opt-in snippet's comment reads "pinned in RAM".
  The 1.20 release notes are amended to point here.
