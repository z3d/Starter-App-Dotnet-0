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
    private readonly IMessageInbox _inbox;

    public InventoryReservationFunction(ILogger<InventoryReservationFunction> logger, IPayloadCaptureSink payloadCaptureSink, TimeProvider timeProvider, IMessageInbox inbox)
    {
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
        _timeProvider = timeProvider;
        _inbox = inbox;
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
        // Resolved once. Calling it again would make up a second correlation id for a message that has none.
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

        await _inbox.ProcessOnceAsync(nameof(InventoryReservationFunction), message.MessageId, (inbox, _) =>
        {
            _logger.LogInformation("Inventory reservation event received. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
                message.MessageId, message.Subject, correlationId);

            // TODO: write the downstream projection on inbox.Connection and inbox.Transaction so it commits once, with the claim.
            // CreateOrderCommandHandler has already reserved the stock. Do not reserve it again.
            return Task.CompletedTask;
        }, cancellationToken);
    }
}
