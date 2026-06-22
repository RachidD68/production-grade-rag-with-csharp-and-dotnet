# SmartDocs.Reranking.Onnx

Self-hosted ONNX cross-encoder reranker for the **SmartDocs** RAG stack — the
companion library to *Production-Grade RAG with C# and .NET*.

Runs `bge-reranker-v2-m3` locally over `Microsoft.ML.OnnxRuntime`. This is the opt-in
package that pulls the native ONNX runtime, kept out of `SmartDocs.Reranking` so every
other consumer stays lean.

## Install

```bash
dotnet add package SmartDocs.Reranking.Onnx
```

## License

MIT © Rachid Dahir
