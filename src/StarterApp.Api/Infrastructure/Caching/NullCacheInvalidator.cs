namespace StarterApp.Api.Infrastructure.Caching;

public class NullCacheInvalidator : ICacheInvalidator
{
    public static readonly NullCacheInvalidator Instance = new();

    public Task InvalidateProductAsync(int productId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InvalidateCustomerAsync(int customerId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
