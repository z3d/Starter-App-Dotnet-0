namespace DockerLearning.ServiceBus.Services;

public interface IServiceBusService
{
    Task<bool> SendMessageAsync<T>(T message, CancellationToken cancellationToken = default) where T : class;
    Task<bool> SendMessagesAsync<T>(IEnumerable<T> messages, CancellationToken cancellationToken = default) where T : class;
    Task<QueueInfo> GetQueueInfoAsync(CancellationToken cancellationToken = default);
    Task<bool> PurgeQueueAsync(CancellationToken cancellationToken = default);
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}

public class QueueInfo
{
    public string QueueName { get; set; } = string.Empty;
    public long ActiveMessageCount { get; set; }
    public long DeadLetterMessageCount { get; set; }
    public long ScheduledMessageCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}