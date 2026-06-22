using System.Runtime.CompilerServices;
using SmartDocs.Reranking;

// Surface the Cohere reranker through this Cohere-scoped package. The type
// physically lives in the SmartDocs.Reranking umbrella assembly, where it
// implements IReranker and the umbrella's reranking DI extension constructs it
// for the 'cohere' mode; forwarding keeps one definition and a real dependency
// edge onto the umbrella.
[assembly: TypeForwardedTo(typeof(CohereReranker))]
