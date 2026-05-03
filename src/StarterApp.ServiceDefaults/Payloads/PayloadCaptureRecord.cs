namespace StarterApp.ServiceDefaults.Payloads;

public sealed class PayloadCaptureRecord
{
    public string OperationId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public int? StatusCode { get; init; }
    public string ArchiveBlobName { get; init; } = string.Empty;
    public string AuditBlobName { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public bool PayloadTruncated { get; init; }
    public long? PayloadSizeBytes { get; init; }
    public int CapturedPayloadBytes { get; init; }
    public string? PayloadSkipReason { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
    public List<PayloadEntityReference> EntityReferences { get; init; } = [];
}
