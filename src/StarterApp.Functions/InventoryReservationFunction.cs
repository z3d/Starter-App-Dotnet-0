using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public class InventoryReservationFunction
{
    private readonly ILogger<InventoryReservationFunction> _logger;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly TimeProvider _timeProvider;

    public InventoryReservationFunction(ILogger<InventoryReservationFunction> logger, IPayloadCaptureSink payloadCaptureSink, TimeProvider timeProvider)
    {
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
        _timeProvider = timeProvider;
    }

    [Function(nameof(InventoryReservationFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("domain-events", "inventory-reservation", Connection = "servicebus")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        CancellationToken cancellationToken)
    {
        var correlationId = MessageSettlement.ResolveCorrelationId(message);
        using var correlationScope = CorrelationContext.Push(correlationId);
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        await MessageSettlement.SettleAsync(message, messageActions, _logger, ProcessAsync, _timeProvider, cancellationToken);
    }

    private async Task ProcessAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
    {
        // RunAsync resolved and pushed the id; resolving again would mint a second one for a
        // message that carries none, splitting its log scope and its archive blob.
        var correlationId = CorrelationContext.GetOrCreate();
        var body = message.Body.ToString();

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "inbound",
            Channel = PayloadCaptureChannels.ServiceBus,
            Operation = nameof(InventoryReservationFunction),
            ContentType = message.ContentType,
            Payload = body,
            Metadata = MessageSettlement.BuildCaptureMetadata(message, "inventory-reservation")
        }, cancellationToken);

        _logger.LogInformation("Inventory reservation event received. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
            message.MessageId, message.Subject, correlationId);

        // CreateOrderCommandHandler reserves catalog stock atomically when creating the order.
        // This subscriber must not change catalog stock, or it would reserve the stock twice.
        // TODO: Deserialize the payload for downstream projections or warehouse notifications.
    }
}
