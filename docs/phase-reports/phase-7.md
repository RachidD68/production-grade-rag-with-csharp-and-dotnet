# Phase 7 — Capstone (Ch 25)

**Status**: ✅ complete

## What was built

- **`SmartDocs.Core/DependencyInjection/RagPipelineRegistration.cs`** — the NuGet shape promised in Ch 25:

  ```csharp
  services.AddSmartDocsRagPipeline(configuration, options =>
  {
      options.UseQdrant("…");
      options.UseNeo4j("…");
      options.EnableCaching("…");
      options.EnableReranking();
      options.EnableAuditing();
  });
  ```

  The registration delegates to `AddSmartDocsCore` and exposes a fluent options surface. Phase 8 polish wires the per-flag service registrations into the relevant projects (`SmartDocs.Performance` for caching, `SmartDocs.Reranking` for reranking, `SmartDocs.Generation/Citations` for auditing).

- **`infra/azure/`** — Bicep templates for one-click Azure deployment:
  - `main.bicep` (subscription scope; creates RG; nests workload)
  - `workload.bicep` (Azure AI Search Standard + Cosmos DB Serverless + Redis Basic + Container Apps env + Container App + Application Insights + Log Analytics)
  - `README.md` (deploy / tear-down)

- **`samples/Ch25_VerticalSliceVariant/`** — same retrieval pipeline as `SmartDocs.Api` but reorganised by feature folder:
  - `Features/Health/HealthFeature.cs` (request + response + endpoint mapper in one file)
  - `Features/Ask/AskFeature.cs` (RegisterServices + MapEndpoints + seeding all in one file)
  - `Program.cs` calls each feature's `RegisterServices` and `MapEndpoints` — no horizontal layering

## Conscious simplifications

- The `RagPipelineRegistration` in Phase 7 only wires `AddSmartDocsCore` and stores option flags. Phase 8 polish (or the chapter author) adds the per-flag conditional service registrations.
- Bicep image reference is `ghcr.io/rachiddahir/smartdocs-api:latest` — readers must build + push the container image first or change the reference.
- The "100-query golden eval as final CI gate" promised in the syllabus is wired only as far as `RetrievalEvaluator` + `GenerationEvaluator`. The CI workflow `ci-eval.yml` still has the Phase-0 placeholder body; Phase 8 polish wires it to invoke `tools/eval-runner` against `data/eval-sets/`.
