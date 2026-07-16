using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Functions;

public class OrderConfirmationEmailFunction
{
    private readonly ILogger<OrderConfirmationEmailFunction> _logger;
    private readonly IPayloadCaptureSink _payloadCaptureSink;

    public OrderConfirmationEmailFunction(ILogger<OrderConfirmationEmailFunction> logger, IPayloadCaptureSink payloadCaptureSink)
    {
        _logger = logger;
        _payloadCaptureSink = payloadCaptureSink;
    }

    [Function(nameof(OrderConfirmationEmailFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("domain-events", "email-notifications", Connection = "servicebus")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        FunctionContext context,
        CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(message);
        using var correlationScope = CorrelationContext.Push(correlationId);
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });

        await MessageSettlement.SettleAsync(message, messageActions, context.RetryContext, _logger, ProcessAsync, cancellationToken);
    }

    private async Task ProcessAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
    {
        var correlationId = ResolveCorrelationId(message);
        var body = message.Body.ToString();

        await _payloadCaptureSink.CaptureAsync(new PayloadCaptureRequest
        {
            CorrelationId = correlationId,
            Direction = "inbound",
            Channel = PayloadCaptureChannels.ServiceBus,
            Operation = nameof(OrderConfirmationEmailFunction),
            ContentType = message.ContentType,
            Payload = body,
            Metadata = BuildCaptureMetadata(message, "email-notifications")
        }, cancellationToken);

        _logger.LogInformation("Order confirmation email triggered. MessageId: {MessageId}, Subject: {Subject}, CorrelationId: {CorrelationId}",
            message.MessageId, message.Subject, correlationId);

        // TODO: Deserialize payload and send confirmation email
        // CONSTRAINT: there is no ordering guarantee into this subscriber — host.json sets
        // maxConcurrentCalls: 16 and the subscription has no sessions, so order.status-changed.v1
        // can be processed BEFORE order.created.v1 for the same order. The real implementation
        // must tolerate out-of-order delivery (e.g. upsert-by-orderId), or the subscription must
        // move to sessions keyed by order id.
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
