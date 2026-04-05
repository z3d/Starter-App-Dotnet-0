namespace StarterApp.Api.Infrastructure.Outbox;

public class OutboxProcessorOptions
{
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public string TopicName { get; set; } = "domain-events";
}
