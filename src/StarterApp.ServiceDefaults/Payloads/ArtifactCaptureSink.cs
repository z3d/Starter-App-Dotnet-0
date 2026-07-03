namespace StarterApp.ServiceDefaults.Payloads;

// Correlation-bound capture for generated artifacts (rendered documents, exports, batch
// files) and significant intermediate transformations. "The payload that arrived" and
// "the artifact we produced" are different evidence; this is the slot for the latter.
// Reuses the full archive/audit/entity-index scheme and the per-channel failure-mode
// policy (PayloadCapture:ArtifactFailureMode) — the API surface is ready before any
// concrete artifact producer exists.
public interface IArtifactCaptureSink
{
    Task CaptureArtifactAsync(ArtifactCaptureRequest request, CancellationToken cancellationToken);

    Task CaptureBinaryArtifactAsync(string artifactName, string stage, string? contentType, byte[] content,
        IReadOnlyList<PayloadEntityReference>? entityReferences, CancellationToken cancellationToken);
}

public sealed class ArtifactCaptureRequest
{
    public string ArtifactName { get; init; } = string.Empty;

    // "generated" for final artifacts, "intermediate" for mid-pipeline transformations.
    public string Stage { get; init; } = ArtifactCaptureSink.GeneratedStage;
    public string? ContentType { get; init; }
    public string Payload { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
    public List<PayloadEntityReference> EntityReferences { get; init; } = [];
}

public sealed class ArtifactCaptureSink : IArtifactCaptureSink
{
    public const string ChannelName = PayloadCaptureChannels.Artifact;
    public const string GeneratedStage = "generated";
    public const string IntermediateStage = "intermediate";

    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly PayloadCaptureOptions _options;

    public ArtifactCaptureSink(IPayloadCaptureSink payloadCaptureSink, Microsoft.Extensions.Options.IOptions<PayloadCaptureOptions> options)
    {
        _payloadCaptureSink = payloadCaptureSink;
        _options = options.Value;
    }

    public async Task CaptureArtifactAsync(ArtifactCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Stage);

        var payload = request.Payload;
        var truncated = false;
        var payloadSizeBytes = (long)System.Text.Encoding.UTF8.GetByteCount(payload);
        if (payloadSizeBytes > _options.MaxPayloadBytes)
        {
            // Mirrors the HTTP middleware's bounded-capture semantics: the audit row says
            // explicitly that this is a bounded capture, not a full-fidelity artifact.
            payload = TruncateToByteBudget(payload, _options.MaxPayloadBytes);
            truncated = true;
        }

        var metadata = new Dictionary<string, string>(request.Metadata)
        {
            ["artifact"] = request.ArtifactName,
            ["stage"] = request.Stage
        };

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = CorrelationContext.GetOrCreate(),
            Direction = "internal",
            Channel = ChannelName,
            Operation = $"{request.Stage} {request.ArtifactName}",
            ContentType = request.ContentType,
            Payload = payload,
            PayloadTruncated = truncated,
            PayloadSizeBytes = payloadSizeBytes,
            CapturedPayloadBytes = System.Text.Encoding.UTF8.GetByteCount(payload),
            PayloadSkipReason = truncated ? $"Artifact exceeded configured limit of {_options.MaxPayloadBytes} bytes" : null,
            Metadata = metadata,
            EntityReferences = request.EntityReferences
        }, cancellationToken);
    }

    public Task CaptureBinaryArtifactAsync(string artifactName, string stage, string? contentType, byte[] content,
        IReadOnlyList<PayloadEntityReference>? entityReferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        return CaptureArtifactAsync(new ArtifactCaptureRequest
        {
            ArtifactName = artifactName,
            Stage = stage,
            ContentType = contentType,
            Payload = Convert.ToBase64String(content),
            Metadata = new Dictionary<string, string> { ["encoding"] = "base64", ["binarySizeBytes"] = content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            EntityReferences = entityReferences is null ? [] : [.. entityReferences]
        }, cancellationToken);
    }

    private static string TruncateToByteBudget(string payload, int maxBytes)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
        if (bytes.Length <= maxBytes)
            return payload;

        // Walk back from the budget edge so a multi-byte character is never split.
        var length = maxBytes;
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
            length--;
        return System.Text.Encoding.UTF8.GetString(bytes, 0, length);
    }
}
