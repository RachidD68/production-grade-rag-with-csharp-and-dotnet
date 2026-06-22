using System.Security.Cryptography;
using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Security.Abstractions;

namespace SmartDocs.Security;

/// <summary>
/// Signs chunks with an HMAC-SHA256 over <c>"{ChunkId}|{DocumentId}|{Text}"</c>,
/// returning the signature as lowercase hex. A verifier holding the same key can
/// recompute the MAC to prove a retrieved chunk was admitted by the trusted
/// ingest path and its text was not altered. The key is injected so it can live
/// in a secrets store / Key Vault rather than in code.
/// </summary>
public sealed class HmacProvenanceSigner : IProvenanceSigner
{
    private readonly byte[] _key;

    public HmacProvenanceSigner(ReadOnlyMemory<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Signing key must be non-empty.", nameof(key));
        }
        _key = key.ToArray();
    }

    public HmacProvenanceSigner(byte[] key)
        : this((key ?? throw new ArgumentNullException(nameof(key))).AsMemory())
    {
    }

    public string Sign(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var payload = Encoding.UTF8.GetBytes($"{chunk.ChunkId}|{chunk.DocumentId}|{chunk.Text}");
        var mac = HMACSHA256.HashData(_key, payload);
        return Convert.ToHexStringLower(mac);
    }
}
