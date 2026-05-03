namespace StarterApp.ServiceDefaults.Payloads;

public sealed class PayloadEntityIndexRecord
{
    public string OperationId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string ArchiveBlobName { get; init; } = string.Empty;
    public string AuditBlobName { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
}
