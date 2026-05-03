using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public class InventoryReservationFunction
{
    private readonly ILogger<InventoryReservationFunction> _logger;
    private readonly IPayloadCaptureSink _payloadCaptureSink;

    public InventoryReservationFunction(ILogger<InventoryReservationFunction> logger, IPayloadCaptureSink payloadCaptureSink)
    {
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
    }

    [Function(nameof(InventoryReservationFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("domain-events", "inventory-reservation", Connection = "servicebus")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(message);
        using var correlationScope = CorrelationContext.Push(correlationId);
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        try
        {
            var body = message.Body.ToString();

            await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
            {
                CorrelationId = correlationId,
                Direction = "inbound",
                Channel = "servicebus",
                Operation = nameof(InventoryReservationFunction),
                ContentType = message.ContentType,
                Payload = body,
                Metadata = new Dictionary<string, string>
                {
                    ["messageId"] = message.MessageId,
                    ["subject"] = message.Subject ?? string.Empty,
                    ["subscription"] = "inventory-reservation",
                    ["topic"] = "domain-events"
                }
            }, cancellationToken);

            _logger.LogInformation("Inventory reservation triggered. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
                message.MessageId, message.Subject, correlationId);

            // TODO: Deserialize payload and reserve inventory
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process inventory reservation. MessageId: {MessageId}, Subject: {Subject}",
                message.MessageId,
                message.Subject);
            throw;
        }
    }

    private static string ResolveCorrelationId(ServiceBusReceivedMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
            return CorrelationContext.Sanitize(message.CorrelationId);

        if (message.ApplicationProperties.TryGetValue(CorrelationContext.ApplicationPropertyName, out var value) && value is string correlationId)
            return CorrelationContext.Sanitize(correlationId);

        return CorrelationContext.Create();
    }
}
