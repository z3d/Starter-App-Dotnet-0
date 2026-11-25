namespace StarterApp.Application.Infrastructure.Worker;

internal sealed class WorkerOperations : IWorkerOperations
{
    private readonly IServiceScopeFactory _scopes;

    public WorkerOperations(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public Task<OrderConfirmation> GetOrderConfirmationAsync(string tenantId, string ownerSubject, Guid orderId, CancellationToken cancellationToken) =>
        RunAsync(tenantId, ownerSubject, "reading the order to confirm", async mediator =>
        {
            var order = await mediator.SendAsync(new GetOrderByIdQuery { Id = orderId }, cancellationToken)
                ?? throw new EntityNotFoundException($"Order with ID {orderId} was not found");
            var customer = await mediator.SendAsync(new GetCustomerQuery(order.CustomerId), cancellationToken)
                ?? throw new EntityNotFoundException($"Customer with ID {order.CustomerId} was not found");
            return new OrderConfirmation(order.Id, order.Status, order.TotalIncludingGst, order.Currency, customer.Name, customer.Email);
        }, cancellationToken);

    private async Task<TResult> RunAsync<TResult>(string tenantId, string ownerSubject, string what, Func<IMediator, Task<TResult>> work, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(ownerSubject))
            throw new WorkerOperationRefusedException($"The event names no tenant or no owner, so {what} was refused.");

        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = _scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // The sample grants a row to its owner alone, so the worker acts inside the owner's scope as a service principal with no scopes of its own.
        services.GetRequiredService<CurrentUserAccessor>().Set(new CurrentUser(ownerSubject, AuthenticatedPrincipalType.Service, tenantId, []));

        try
        {
            return await work(services.GetRequiredService<IMediator>());
        }
        catch (Exception ex) when (IsRefusal(ex))
        {
            throw new WorkerOperationRefusedException($"The application refused {what} ({ex.GetType().Name}).", ex);
        }
    }

    // A redelivery cannot change a 400, 403, 404 or 422, nor a rule the domain holds. The 409 of two writers meeting, and a fault, can.
    internal static bool IsRefusal(Exception exception) =>
        exception is DomainRuleException
        || WebApplicationExtensions.ResolveExceptionStatusCode(exception) is StatusCodes.Status400BadRequest or StatusCodes.Status403Forbidden
            or StatusCodes.Status404NotFound or StatusCodes.Status422UnprocessableEntity;
}
