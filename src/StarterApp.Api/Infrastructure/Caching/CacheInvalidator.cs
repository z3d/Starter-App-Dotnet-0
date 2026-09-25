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
    private readonly ILogger<CacheInvalidator> _logger;

    public CacheInvalidator(IDistributedCache cache, ICurrentUser currentUser, ILogger<CacheInvalidator> logger)
    {
        _cache = cache;
        _currentUser = currentUser;
        _logger = logger;
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
        // Only owner-scoped keys are ever written; this guard is for test harnesses that invalidate without an identity.
        if (!_currentUser.IsAuthenticated)
            return;

        await InvalidateKeyAsync(OwnerScopedCacheKey.Create(cacheKey, _currentUser), cancellationToken);
    }

    private async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken)
    {
        // Fails open: a cache outage must not turn a committed write into a 500, and each step is an independent defence.
        try
        {
            // Generation first: readers validate it even when the removal below fails.
            await _cache.SetStringAsync(
                CacheTombstone.KeyFor(cacheKey),
                Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTombstone.Ttl },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache generation advance failed for {CacheKey}; evicting the entry directly.", cacheKey);
        }

        try
        {
            // Evict unconditionally: this is what stops a deleted row being served when the generation write did not land.
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for {CacheKey}; the entry may be served stale until its TTL expires.", cacheKey);
        }
    }
}
