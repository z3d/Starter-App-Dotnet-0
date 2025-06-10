namespace DockerLearning.ServiceBus.Configuration;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ConnectionString { get; set; } = string.Empty;
    public string QueueName { get; set; } = "products";
    public string DeadLetterQueueName { get; set; } = "products-dlq";
    public int MaxConcurrentCalls { get; set; } = 10;
    public int MaxAutoLockRenewalDuration { get; set; } = 300; // 5 minutes
    public int PrefetchCount { get; set; } = 50;
    public bool EnableAutoComplete { get; set; } = false;
}