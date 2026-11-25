namespace StarterApp.Application.Infrastructure.Worker;

// Everything the Functions worker may ask of the application. Each call runs a request through the mediator for one tenant and owner, in this process.
public interface IWorkerOperations
{
    Task<OrderConfirmation> GetOrderConfirmationAsync(string tenantId, string ownerSubject, Guid orderId, CancellationToken cancellationToken);
}
