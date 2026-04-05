using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

public class InventoryReservationFunction
{
    private readonly ILogger<InventoryReservationFunction> _logger;

    public InventoryReservationFunction(ILogger<InventoryReservationFunction> logger)
    {
        _logger = logger;
    }

    [Function(nameof(InventoryReservationFunction))]
    public Task RunAsync(
        [ServiceBusTrigger("domain-events", "inventory-reservation", Connection = "servicebus")]
        ServiceBusReceivedMessage message)
    {
        try
        {
            var body = message.Body.ToString();

            _logger.LogInformation(
                "Inventory reservation triggered. MessageId: {MessageId}, Subject: {Subject}, Body: {Body}",
                message.MessageId,
                message.Subject,
                body);

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

        return Task.CompletedTask;
    }
}
