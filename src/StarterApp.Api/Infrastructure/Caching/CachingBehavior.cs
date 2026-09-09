using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace StarterApp.Api.Infrastructure.Caching;

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Keys currently being early-recomputed (in-process). Bounded by in-flight refreshes:
    // entries are removed in finally. Single-flight is per replica — cross-replica the
    // herd is reduced to one request per replica, which is the acceptable residual.
    private static readonly ConcurrentDictionary<string, byte> RefreshesInFlight = new();

    private readonly IDistributedCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(IDistributedCache cache, ICurrentUser currentUser, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next();

        // An invalidation generation must outlive every value that observed its predecessor.
        // Pin expiry before the database read (and even before cache I/O), so slow publishers
        // cannot extend that value beyond the generation's retention window.
        if (cacheable.CacheDuration > CacheTombstone.Ttl)
            return await next();

        // Owner-scoped values must never share a key across identities. Without an authenticated
        // subject there is nothing to scope by, so serve uncached rather than fall back to a global
        // key that a later authenticated reader could hit.
        if (cacheable is IOwnerScopedRequest && !_currentUser.IsAuthenticated)
            return await next();

        var expiresAtUtc = DateTimeOffset.UtcNow + cacheable.CacheDuration;
        var cacheKey = ResolveCacheKey(cacheable);
        var cached = (await TryGetAsync(cacheKey, cancellationToken)).Value;
        var (generationKnown, generation) = await TryGetAsync(CacheTombstone.KeyFor(cacheKey), cancellationToken);
        // Old replicas wrote a constant tombstone. It cannot distinguish successive invalidations.
        generationKnown &= generation != "1";
        if (cached is not null)
        {
            var envelope = TryDeserializeEnvelope(cached);
            if (envelope is not null && generationKnown && envelope.Generation == generation &&
                DateTimeOffset.UtcNow < envelope.ExpiresAtUtc)
            {
                if (cacheable.CacheRefreshWindow <= TimeSpan.Zero || DateTimeOffset.UtcNow < envelope.RefreshAfterUtc)
                {
                    _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                    return envelope.Value!;
                }

                // One request refreshes inline to preserve the caller's identity for owner-scoped keys.
                // Concurrent requests keep the cached value while it is still valid.
                if (!RefreshesInFlight.TryAdd(cacheKey, 0))
                {
                    _logger.LogDebug("Refresh already in flight for {CacheKey}; serving cached value", cacheKey);
                    return envelope.Value!;
                }

                try
                {
                    _logger.LogDebug("Refresh-ahead recompute for {CacheKey}", cacheKey);
                    var refreshed = await next();
                    if (refreshed is not null)
                        await StoreAsync(cacheKey, cacheable, refreshed, generation, expiresAtUtc, cancellationToken);
                    return refreshed;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && DateTimeOffset.UtcNow < envelope.ExpiresAtUtc)
                {
                    // If refresh fails, serve the cached value while it is still within its TTL.
                    // A cache miss has no fallback and still propagates the error.
                    _logger.LogWarning(ex, "Refresh-ahead recompute failed for {CacheKey}; serving the cached value until the next attempt", cacheKey);
                    return envelope.Value!;
                }
                finally
                {
                    RefreshesInFlight.TryRemove(cacheKey, out _);
                }
            }

            // A superseded generation or pinned expiry is the normal post-invalidation miss;
            // unreadable or pre-envelope content is the unusual one. Log them apart so an
            // operator does not read routine invalidation as cache corruption.
            if (envelope is null)
                _logger.LogDebug("Cache entry for {CacheKey} is not a valid envelope; treating as miss", cacheKey);
            else
                _logger.LogDebug("Cache entry for {CacheKey} was invalidated or expired; treating as miss", cacheKey);
        }

        var result = await next();

        if (result is not null && generationKnown)
            await StoreAsync(cacheKey, cacheable, result, generation, expiresAtUtc, cancellationToken);

        return result;
    }

    // The cache is best-effort infrastructure: a Redis outage must degrade to uncached database
    // reads, never turn a healthy read into a 500 (before or after PostgreSQL has answered).
    // Cancellation always propagates. Mirrors the fail-open posture of CacheInvalidator.
    private async Task<(bool Success, string? Value)> TryGetAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            return (true, await _cache.GetStringAsync(cacheKey, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache read failed for {CacheKey}; treating as a miss", cacheKey);
            return (false, null);
        }
    }

    private async Task StoreAsync(string cacheKey, ICacheable cacheable, TResponse result,
        string? generation, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        var (known, currentGeneration) = await TryGetAsync(CacheTombstone.KeyFor(cacheKey), cancellationToken);
        if (!known || currentGeneration != generation || DateTimeOffset.UtcNow >= expiresAtUtc)
            return;

        // The pre-write check only avoids needless obsolete writes. Correctness comes from storing
        // the generation observed BEFORE the handler and checking it on every hit. An invalidation
        // can complete between this check and SetAsync; that publication is still rejected on reads.
        var refreshAfterUtc = expiresAtUtc - cacheable.CacheRefreshWindow;
        var serialized = JsonSerializer.Serialize(new CacheEnvelope(result, refreshAfterUtc, expiresAtUtc, generation));
        var options = new DistributedCacheEntryOptions { AbsoluteExpiration = expiresAtUtc };

        try
        {
            await _cache.SetStringAsync(cacheKey, serialized, options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write failed for {CacheKey}; the handler result is returned uncached", cacheKey);
        }
    }

    private static CacheEnvelope? TryDeserializeEnvelope(string cached)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(cached);
            return envelope is not null && envelope.RefreshAfterUtc != default && envelope.ExpiresAtUtc != default
                ? envelope
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string ResolveCacheKey(ICacheable cacheable)
    {
        return cacheable is IOwnerScopedRequest
            ? OwnerScopedCacheKey.Create(cacheable.CacheKey, _currentUser)
            : cacheable.CacheKey;
    }

    private sealed record CacheEnvelope(TResponse? Value, DateTimeOffset RefreshAfterUtc, DateTimeOffset ExpiresAtUtc, string? Generation);
}
