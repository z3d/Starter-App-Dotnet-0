namespace StarterApp.Application.Infrastructure.Worker;

// With no database connection string the host has no application to call, so every operation is refused and its message dead-letters.
internal sealed class UnconfiguredWorkerOperations : IWorkerOperations
{
    public Task<OrderConfirmation> GetOrderConfirmationAsync(string tenantId, string ownerSubject, Guid orderId, CancellationToken cancellationToken) => throw Refused();

    private static WorkerOperationRefusedException Refused() =>
        new("ConnectionStrings:database is not configured, so the worker has no application to call.");
}
