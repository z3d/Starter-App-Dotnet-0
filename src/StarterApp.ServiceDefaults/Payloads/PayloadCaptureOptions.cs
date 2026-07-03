using System.ComponentModel.DataAnnotations;

namespace StarterApp.ServiceDefaults.Payloads;

public enum PayloadCaptureFailureMode
{
    FailOpen,
    FailClosed
}

public class PayloadCaptureOptions
{
    public bool Enabled { get; set; } = true;

    public bool RequireArchiveStore { get; set; }

    // Failure policy is per-channel because the trade-offs differ. Payload capture is an audit
    // sidecar and must NOT take down synchronous user traffic, so HTTP defaults to FailOpen
    // (a capture failure is logged and the request proceeds). The Service Bus / outbox path runs
    // in the background, decoupled from users, so it can default safely and be tightened to
    // FailClosed in production-like config (no event is published without a durable audit record;
    // OutboxProcessor pauses the batch on capture failure rather than poisoning the message).
    // Both default to FailOpen in code so standalone dev / tests with no archive store never break;
    // the AppHost production-like orchestration sets ServiceBusFailureMode=FailClosed.
    public PayloadCaptureFailureMode HttpFailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

    public PayloadCaptureFailureMode ServiceBusFailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

    // Generated artifacts and intermediate transformations (channel "artifact") run inside
    // request/handler flows, so they default FailOpen like HTTP; a compliance domain whose
    // artifacts ARE the deliverable can opt into FailClosed.
    public PayloadCaptureFailureMode ArtifactFailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

    public PayloadCaptureFailureMode FailureModeFor(string? channel)
    {
        if (string.Equals(channel, PayloadCaptureChannels.Http, StringComparison.OrdinalIgnoreCase))
            return HttpFailureMode;
        if (string.Equals(channel, PayloadCaptureChannels.Artifact, StringComparison.OrdinalIgnoreCase))
            return ArtifactFailureMode;
        return ServiceBusFailureMode;
    }

    [Required, MinLength(1)]
    public string ContainerName { get; set; } = "payload-observability";

    public string? ConnectionString { get; set; }
    public string? AccountUri { get; set; }

    [Required, MinLength(1)]
    public string ArchivePrefix { get; set; } = "archive";

    [Required, MinLength(1)]
    public string AuditPrefix { get; set; } = "audit";

    [Required, MinLength(1)]
    public string EntityIndexPrefix { get; set; } = "entity-index";

    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    [Range(1, 10000)]
    public int CleanupBatchSize { get; set; } = 500;

    [Range(1, 104_857_600)]
    public int MaxPayloadBytes { get; set; } = 1_048_576;

    // Bounds how many entity-index references a single capture may fan out into; each reference is a
    // separate serial blob round trip on the request thread, so this caps request-path amplification
    // from a hostile body packed with distinct *Id properties.
    [Range(1, 4096)]
    public int MaxEntityReferences { get; set; } = PayloadEntityReferenceExtractor.DefaultMaxEntityReferences;

    public string[] CapturedContentTypes { get; set; } =
    [
        "application/json",
        "application/*+json",
        "text/json",
        "text/plain"
    ];

    // Shared by the JSON payload redactor (log masking), the capture sink (metadata/query-string
    // masking), and the entity-reference extractor (blob-path exclusion) — matched as normalized
    // substrings, so "ssn" also covers "ssnId" and "license" covers "driversLicenseNumber".
    internal static readonly string[] DefaultSensitivePropertyNames =
    [
        "address",
        "authorization",
        "cookie",
        "email",
        "license",
        "medicare",
        "name",
        "national",
        "passport",
        "password",
        "phone",
        "secret",
        "set-cookie",
        "ssn",
        "tax",
        "token"
    ];

    public string[] SensitivePropertyNames { get; set; } = DefaultSensitivePropertyNames;
}
