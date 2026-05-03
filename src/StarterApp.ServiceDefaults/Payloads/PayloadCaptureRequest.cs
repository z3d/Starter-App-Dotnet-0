namespace StarterApp.ServiceDefaults.Payloads;

public sealed class PayloadCaptureRequest
{
    public string? CorrelationId { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string? ContentType { get; init; }
    public string Payload { get; init; } = string.Empty;
    public int? StatusCode { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = [];
    public List<PayloadEntityReference> EntityReferences { get; init; } = [];
}
