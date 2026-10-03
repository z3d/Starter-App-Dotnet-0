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

    // HTTP stays FailOpen; the AppHost sets the Service Bus channel FailClosed and the outbox pauses until capture succeeds.
    public PayloadCaptureFailureMode HttpFailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

    public PayloadCaptureFailureMode ServiceBusFailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

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

    // Must fit inside the Consumption plan's 5-minute functionTimeout; raise the two together in host.json.
    [Range(1, 3600)]
    public int CleanupTimeBudgetSeconds { get; set; } = 300;

    [Range(1, 104_857_600)]
    public int MaxPayloadBytes { get; set; } = 1_048_576;

    // A hostile body packed with distinct *Id properties would otherwise fan out without limit.
    [Range(1, 4096)]
    public int MaxEntityReferences { get; set; } = PayloadEntityReferenceExtractor.DefaultMaxEntityReferences;

    [Range(1_048_576, 1_073_741_824)]
    public int BackgroundQueueMaxBytes { get; set; } = 32 * 1024 * 1024;

    [Range(1, 600)]
    public int BackgroundWriteTimeoutSeconds { get; set; } = 30;

    public string[] CapturedContentTypes { get; set; } =
    [
        "application/json",
        "application/*+json",
        "text/json",
        "text/plain"
    ];

    // Matched as normalized substrings, so "ssn" also covers "ssnId".
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
