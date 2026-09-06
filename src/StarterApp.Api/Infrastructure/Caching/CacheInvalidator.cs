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
        // Only the owner-scoped key is ever written: cacheable queries are owner-scoped and
        // the protected surface is unreachable without an authenticated identity, so the bare key
        // has no writer and needs no invalidation. Mutations always run authenticated; the
        // guard below is a belt for test harnesses that invalidate without an identity.
        if (!_currentUser.IsAuthenticated)
            return;

        await InvalidateKeyAsync(OwnerScopedCacheKey.Create(cacheKey, _currentUser), cancellationToken);
    }

    private async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            // Advance the generation first. Readers validate it even when removal races a
            // pending publication or fails; every mutation needs a distinct value.
            await _cache.SetStringAsync(
                CacheTombstone.KeyFor(cacheKey),
                Guid.NewGuid().ToString("N"),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTombstone.Ttl },
                cancellationToken);
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Caching is a best-effort sidecar (it falls back to in-memory when Redis is absent), so a
            // transient cache outage must not turn an already-committed write into a 500. Log and
            // continue; the stale entry self-heals at its TTL.
            _logger.LogWarning(ex, "Cache invalidation failed for {CacheKey}; the entry may be served stale until its TTL expires.", cacheKey);
        }
    }
}
