using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartDocs.Operations.Compliance;

/// <summary>
/// Per-store breakdown line on an erasure receipt. Exactly one of
/// <see cref="RecordsErased"/> / <see cref="RecordsRedacted"/> is set, depending
/// on whether the store hard-deletes (vector, graph) or redacts in place
/// (audit-log PII).
/// </summary>
public sealed record ErasureStoreResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("recordsErased")] int? RecordsErased,
    [property: JsonPropertyName("recordsRedacted")] int? RecordsRedacted,
    [property: JsonPropertyName("completedAt")] DateTimeOffset CompletedAt);

/// <summary>
/// The signed receipt a subject (or regulator) gets back after a GDPR Article 17
/// erasure. Composes the Ch 22 <see cref="GdprDeletionPipeline"/> output (the
/// per-store record counts) with a detached signature over the receipt's
/// canonical bytes, so the receipt is tamper-evident. JSON shape matches the
/// chapter:
/// <c>{ requestId, subjectId, receivedAt, completedAt, slaMet, stores[], signature }</c>.
/// </summary>
public sealed record ErasureReceipt(
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("subjectId")] string SubjectId,
    [property: JsonPropertyName("receivedAt")] DateTimeOffset ReceivedAt,
    [property: JsonPropertyName("completedAt")] DateTimeOffset CompletedAt,
    [property: JsonPropertyName("slaMet")] bool SlaMet,
    [property: JsonPropertyName("stores")] IReadOnlyList<ErasureStoreResult> Stores,
    [property: JsonPropertyName("signature")] string Signature);

/// <summary>
/// Builds and signs an <see cref="ErasureReceipt"/> from a completed
/// <see cref="DeletionAuditEntry"/>. The signature is computed over the
/// receipt's canonical JSON with the <c>signature</c> field blanked, using an
/// injected signer (HMAC-SHA256 hex by default), mirroring the Ch 23 provenance
/// convention. SLA is met when completion lands within the configured window of
/// receipt.
/// </summary>
public sealed class ErasureReceiptGenerator
{
    private static readonly JsonSerializerOptions CanonicalJson = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Func<byte[], string> _sign;
    private readonly TimeSpan _sla;

    public ErasureReceiptGenerator(Func<byte[], string> sign, TimeSpan? sla = null)
    {
        ArgumentNullException.ThrowIfNull(sign);
        _sign = sign;
        _sla = sla ?? TimeSpan.FromDays(30); // GDPR's one-month default.
    }

    /// <summary>HMAC-SHA256 signer over the canonical bytes (lowercase hex).</summary>
    public static Func<byte[], string> HmacSigner(ReadOnlyMemory<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("Signing key must be non-empty.", nameof(key));
        }
        var keyBytes = key.ToArray();
        return bytes => Convert.ToHexStringLower(HMACSHA256.HashData(keyBytes, bytes));
    }

    /// <summary>
    /// Build a signed receipt from the pipeline's <paramref name="deletion"/>
    /// result. <paramref name="requestId"/> ties back to the inbound erasure
    /// request; <paramref name="receivedAt"/> is when the request arrived (used
    /// for the SLA check). <paramref name="redactedAuditRecords"/> optionally adds
    /// an audit-log redaction line for PII scrubbed rather than hard-deleted.
    /// </summary>
    public ErasureReceipt Build(
        string requestId,
        DateTimeOffset receivedAt,
        DeletionAuditEntry deletion,
        int redactedAuditRecords = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(deletion);

        var stores = new List<ErasureStoreResult>
        {
            new("vector-store", RecordsErased: deletion.ChunkIds.Count, RecordsRedacted: null, deletion.CompletedUtc),
            new("graph-store", RecordsErased: deletion.GraphEntityIds.Count, RecordsRedacted: null, deletion.CompletedUtc),
        };
        if (redactedAuditRecords > 0)
        {
            stores.Add(new("audit-log", RecordsErased: null, RecordsRedacted: redactedAuditRecords, deletion.CompletedUtc));
        }

        var slaMet = deletion.CompletedUtc - receivedAt <= _sla;

        // Sign the receipt with an empty signature placeholder so the signed
        // bytes are deterministic and independent of the signature itself.
        var unsigned = new ErasureReceipt(
            RequestId: requestId,
            SubjectId: deletion.SubjectId,
            ReceivedAt: receivedAt,
            CompletedAt: deletion.CompletedUtc,
            SlaMet: slaMet,
            Stores: stores,
            Signature: string.Empty);

        var canonical = JsonSerializer.SerializeToUtf8Bytes(unsigned, CanonicalJson);
        var signature = _sign(canonical);

        return unsigned with { Signature = signature };
    }

    /// <summary>
    /// Verify a receipt's signature by recomputing it over the canonical bytes
    /// (signature blanked) with the same signer.
    /// </summary>
    public bool Verify(ErasureReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var canonical = JsonSerializer.SerializeToUtf8Bytes(receipt with { Signature = string.Empty }, CanonicalJson);
        var expected = _sign(canonical);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(receipt.Signature));
    }

    /// <summary>Serialize a receipt to the chapter's pretty JSON shape.</summary>
    public static string ToJson(ErasureReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return JsonSerializer.Serialize(receipt, PrettyJson);
    }
}
