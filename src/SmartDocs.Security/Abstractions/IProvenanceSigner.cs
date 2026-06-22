using SmartDocs.Core.Documents;

namespace SmartDocs.Security.Abstractions;

/// <summary>
/// Produces a tamper-evident provenance signature for a <see cref="DocumentChunk"/>
/// at ingest time. The signature travels with the chunk (see
/// <see cref="DocumentChunk.Provenance"/>) so downstream stages can prove that a
/// retrieved chunk was admitted through the trusted ingest path and was not
/// altered or injected afterwards.
/// </summary>
public interface IProvenanceSigner
{
    /// <summary>Returns the provenance signature for <paramref name="chunk"/>.</summary>
    string Sign(DocumentChunk chunk);
}
