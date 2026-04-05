using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

public class OrderConfirmationEmailFunction
{
    private readonly ILogger<OrderConfirmationEmailFunction> _logger;

    public OrderConfirmationEmailFunction(ILogger<OrderConfirmationEmailFunction> logger)
    {
        _logger = logger;
    }

    [Function(nameof(OrderConfirmationEmailFunction))]
    public Task RunAsync(
        [ServiceBusTrigger("domain-events", "email-notifications", Connection = "servicebus")]
        ServiceBusReceivedMessage message)
    {
        _logger.LogInformation(
            "Order confirmation email triggered. MessageId: {MessageId}, Subject: {Subject}, Body: {Body}",
            message.MessageId,
            message.Subject,
            message.Body.ToString());

        return Task.CompletedTask;
    }
}
