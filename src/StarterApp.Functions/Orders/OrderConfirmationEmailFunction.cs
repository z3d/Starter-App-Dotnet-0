using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.Application.Infrastructure.Worker;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public class OrderConfirmationEmailFunction
{
    private readonly ILogger<OrderConfirmationEmailFunction> _logger;
    private readonly IPayloadCaptureSink _payloadCaptureSink;
    private readonly TimeProvider _timeProvider;
    private readonly IMessageInbox _inbox;
    private readonly IWorkerOperations _operations;

    public OrderConfirmationEmailFunction(ILogger<OrderConfirmationEmailFunction> logger, IPayloadCaptureSink payloadCaptureSink, TimeProvider timeProvider, IMessageInbox inbox, IWorkerOperations operations)
    {
        _operations = operations;
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
        _timeProvider = timeProvider;
        _inbox = inbox;
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
        // Resolved once. Calling it again would make up a second correlation id for a message that has none.
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

        var order = OrderEventReference.Parse(body);

        await _inbox.ProcessOnceAsync(nameof(OrderConfirmationEmailFunction), message.MessageId, async (_, token) =>
        {
            var confirmation = await _operations.GetOrderConfirmationAsync(order.TenantId, order.OwnerSubject, order.OrderId, token);

            _logger.LogInformation("Order confirmation email triggered. MessageId: {MessageId}, Subject: {Subject}, OrderId: {OrderId}, Status: {Status}, CorrelationId: {CorrelationId}",
                message.MessageId, message.Subject, confirmation.OrderId, confirmation.Status, correlationId);

            // TODO: send the confirmation to confirmation.CustomerEmail. Messages arrive out of order (16 at a time, no sessions), so a status change can land before the order-created event.
            // An email cannot be part of the inbox transaction. If the send succeeds and the commit then fails, the redelivery sends it again.
        }, cancellationToken);
    }
}
