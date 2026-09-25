using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Asking for a batch round-trips the AMQP link without sending anything.
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
