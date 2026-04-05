using Microsoft.Extensions.Caching.Distributed;

namespace StarterApp.Api.Infrastructure.Caching;

public interface ICacheInvalidator
{
    Task InvalidateProductAsync(int productId, CancellationToken cancellationToken = default);
    Task InvalidateCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}

public class CacheInvalidator : ICacheInvalidator
{
    private readonly IDistributedCache _cache;

    public CacheInvalidator(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task InvalidateProductAsync(int productId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"Product:{productId}", cancellationToken);
    }

    public async Task InvalidateCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync($"Customer:{customerId}", cancellationToken);
    }
}
