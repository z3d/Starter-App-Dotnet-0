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

    [Required, MinLength(1)]
    public string TopicName { get; set; } = "domain-events";
}
