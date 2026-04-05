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
        try
        {
            var body = message.Body.ToString();

            _logger.LogInformation(
                "Order confirmation email triggered. MessageId: {MessageId}, Subject: {Subject}, Body: {Body}",
                message.MessageId,
                message.Subject,
                body);

            // TODO: Deserialize payload and send confirmation email
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process email notification. MessageId: {MessageId}, Subject: {Subject}",
                message.MessageId,
                message.Subject);
            throw;
        }

        return Task.CompletedTask;
    }
}
