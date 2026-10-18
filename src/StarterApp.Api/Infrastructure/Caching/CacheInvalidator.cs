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
        // Only owner-scoped keys are ever written. This guard is for tests that invalidate without a signed-in user.
        if (!_currentUser.IsAuthenticated)
            return;

        await InvalidateKeyAsync(OwnerScopedCacheKey.Create(cacheKey, _currentUser), cancellationToken);
    }

    private async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken)
    {
        // Fails open. A cache outage must not turn a committed write into a 500. Each step below stands on its own.
        try
        {
            // Write the generation first. Readers check it even if the removal below fails.
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
            // Always remove the value. This is what stops a deleted row being served when the generation write failed.
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache invalidation failed for {CacheKey}; the entry may be served stale until its TTL expires.", cacheKey);
        }
    }
}
