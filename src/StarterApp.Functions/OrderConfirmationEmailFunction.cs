using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public class OrderConfirmationEmailFunction
{
    private readonly ILogger<OrderConfirmationEmailFunction> _logger;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly TimeProvider _timeProvider;

    public OrderConfirmationEmailFunction(ILogger<OrderConfirmationEmailFunction> logger, IPayloadCaptureSink payloadCaptureSink, TimeProvider timeProvider)
    {
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
        _timeProvider = timeProvider;
    }

    [Function(nameof(OrderConfirmationEmailFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("domain-events", "email-notifications", Connection = "servicebus")]
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
            Operation = nameof(OrderConfirmationEmailFunction),
            ContentType = message.ContentType,
            Payload = body,
            Metadata = MessageSettlement.BuildCaptureMetadata(message, "email-notifications")
        }, cancellationToken);

        _logger.LogInformation("Order confirmation email triggered. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
            message.MessageId, message.Subject, correlationId);

        // TODO: Deserialize the payload and send the confirmation email.
        // Delivery is unordered: host.json allows 16 concurrent calls and the subscription has no
        // sessions, so a status change can arrive before the order-created event for the same
        // order. The implementation must cope with either order (for example, upsert by order ID),
        // or the subscription must move to sessions keyed by order ID.
    }
}
