using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Probes the Service Bus namespace by opening a sender link to the domain-events topic and asking
// for a message batch — that round-trips the AMQP link (the service reports the max batch size)
// without sending anything, so the probe has no side effects on the topic.
public sealed class ServiceBusHealthCheck : IHealthCheck
{
    private readonly ServiceBusClient _client;
    private readonly OutboxProcessorOptions _options;

    public ServiceBusHealthCheck(ServiceBusClient client, IOptions<OutboxProcessorOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sender = _client.CreateSender(_options.TopicName);
            using var batch = await sender.CreateMessageBatchAsync(cancellationToken);
            return HealthCheckResult.Healthy("Service Bus is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Service Bus is unreachable", ex);
        }
    }
}
