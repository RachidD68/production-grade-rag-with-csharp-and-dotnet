namespace SmartDocs.Core.Documents;

/// <summary>
/// A bounded slice of a <see cref="Document"/>'s text, ready to be embedded.
/// Carries enough provenance information to render a citation pointing back
/// at the source document.
/// </summary>
/// <param name="ChunkId">
/// Stable identifier of the form <c>{DocumentId}#{ChunkIndex}</c>.
/// </param>
/// <param name="DocumentId">The id of the parent <see cref="DocumentMetadata"/>.</param>
/// <param name="ChunkIndex">Zero-based ordinal of this chunk within the parent document.</param>
/// <param name="Text">The chunk's textual content.</param>
/// <param name="StartCharOffset">Inclusive UTF-16 character offset into the parent <see cref="Document.Content"/>.</param>
/// <param name="EndCharOffset">Exclusive UTF-16 character offset into the parent <see cref="Document.Content"/>.</param>
/// <param name="Metadata">A copy of the parent document's metadata, denormalised onto the chunk for filtering.</param>
public sealed record DocumentChunk(
    string ChunkId,
    string DocumentId,
    int ChunkIndex,
    string Text,
    int StartCharOffset,
    int EndCharOffset,
    DocumentMetadata Metadata);
