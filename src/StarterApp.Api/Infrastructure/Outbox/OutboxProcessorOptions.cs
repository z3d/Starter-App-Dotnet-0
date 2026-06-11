using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxProcessorOptions
{
    [Range(1, 3600)]
    public int PollingIntervalSeconds { get; set; } = 5;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 20;

    [Range(1, 100)]
    public int MaxRetries { get; set; } = 3;

    [Range(1, 3600)]
    public int LockDurationSeconds { get; set; } = 60;

    [Range(1, 1440)]
    public int HealthRowIntervalMinutes { get; set; } = 15;

    [Required, MinLength(1)]
    public string TopicName { get; set; } = "domain-events";

    // Processed/errored rows keep full event payloads; without retention the table grows forever
    // (the blob archive has a cleanup story — the outbox table needs one too).
    [Range(1, 3650)]
    public int RetentionDays { get; set; } = 30;

    [Range(1, 1440)]
    public int CleanupIntervalMinutes { get; set; } = 60;
}
