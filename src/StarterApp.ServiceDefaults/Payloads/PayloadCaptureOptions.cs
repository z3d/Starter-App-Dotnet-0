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

    public PayloadCaptureFailureMode FailureMode { get; set; } = PayloadCaptureFailureMode.FailOpen;

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

    public string CleanupCron { get; set; } = "0 0 * * * *";

    [Range(1, 104_857_600)]
    public int MaxPayloadBytes { get; set; } = 1_048_576;

    public string[] CapturedContentTypes { get; set; } =
    [
        "application/json",
        "application/*+json",
        "text/json",
        "text/plain"
    ];

    public string[] SensitivePropertyNames { get; set; } =
    [
        "address",
        "authorization",
        "cookie",
        "email",
        "name",
        "password",
        "phone",
        "secret",
        "set-cookie",
        "ssn",
        "token"
    ];
}
