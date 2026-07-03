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
                Channel = PayloadCaptureChannels.ServiceBus,
                Operation = nameof(InventoryReservationFunction),
                ContentType = message.ContentType,
                Payload = body,
                Metadata = BuildCaptureMetadata(message, "inventory-reservation")
            }, cancellationToken);

            _logger.LogInformation("Inventory reservation event received. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
                message.MessageId, message.Subject, correlationId);

            // NOTE: Catalog stock is reserved synchronously and atomically by
            // CreateOrderCommandHandler (UPDATE products SET stock = stock - qty WHERE stock >= qty)
            // inside the same unit of work that creates the order. That handler is the single owner
            // of catalog stock mutation.
            //
            // This subscriber is notification/projection-only. Do NOT decrement or otherwise mutate
            // catalog stock here — doing so would double-reserve. Use this hook for downstream
            // projections, warehouse/fulfilment integration, or notifications instead.
            // TODO: Deserialize payload and build the downstream inventory projection (read-only w.r.t. catalog stock).
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

    // Replayed/resubmitted messages keep their marker in the inbound capture: for dead-letter
    // resubmits this captured record is the only durable artifact of the redelivery.
    private static Dictionary<string, string> BuildCaptureMetadata(ServiceBusReceivedMessage message, string subscription)
    {
        var metadata = new Dictionary<string, string>
        {
            ["messageId"] = message.MessageId,
            ["subject"] = message.Subject ?? string.Empty,
            ["subscription"] = subscription,
            ["topic"] = "domain-events"
        };

        if (message.ApplicationProperties.TryGetValue("Replay", out var replay) && replay is true)
        {
            metadata["replay"] = "true";
            if (message.ApplicationProperties.TryGetValue("ReplayCount", out var count))
                metadata["replayCount"] = count?.ToString() ?? "1";
        }

        return metadata;
    }
}
