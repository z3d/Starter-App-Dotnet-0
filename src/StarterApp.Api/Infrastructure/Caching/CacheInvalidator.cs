using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;

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

    public CacheInvalidator(IDistributedCache cache)
        : this(cache, CurrentUser.Anonymous, NullLogger<CacheInvalidator>.Instance)
    {
    }

    public CacheInvalidator(IDistributedCache cache, ICurrentUser currentUser)
        : this(cache, currentUser, NullLogger<CacheInvalidator>.Instance)
    {
    }

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
        await InvalidateKeyAsync(cacheKey, cancellationToken);

        if (_currentUser.IsAuthenticated)
            await InvalidateKeyAsync(OwnerScopedCacheKey.Create(cacheKey, _currentUser), cancellationToken);
    }

    private async Task InvalidateKeyAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);

            // Tombstone the key so a concurrent read that already missed the cache (and is holding a
            // pre-write value) does not repopulate it after this invalidation. The reader checks the
            // tombstone immediately before its SetString.
            await _cache.SetStringAsync(
                CacheTombstone.KeyFor(cacheKey),
                "1",
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTombstone.Ttl },
                cancellationToken);
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
