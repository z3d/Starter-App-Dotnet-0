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
    private readonly ICurrentUser _currentUser;

    public CacheInvalidator(IDistributedCache cache)
        : this(cache, CurrentUser.Anonymous)
    {
    }

    public CacheInvalidator(IDistributedCache cache, ICurrentUser currentUser)
    {
        _cache = cache;
        _currentUser = currentUser;
    }

    public async Task InvalidateProductAsync(int productId, CancellationToken cancellationToken)
    {
        await RemoveAsync($"Product:{productId}", cancellationToken);
    }

    public async Task InvalidateCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        await RemoveAsync($"Customer:{customerId}", cancellationToken);
    }

    private async Task RemoveAsync(string cacheKey, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        if (_currentUser.IsAuthenticated)
            await _cache.RemoveAsync(OwnerScopedCacheKey.Create(cacheKey, _currentUser), cancellationToken);
    }
}
