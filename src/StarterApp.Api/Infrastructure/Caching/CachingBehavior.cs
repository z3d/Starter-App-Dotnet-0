using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace StarterApp.Api.Infrastructure.Caching;

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Makes sure only one early recompute runs per key on this replica. Entries are removed in the finally block.
    private static readonly ConcurrentDictionary<string, byte> RefreshesInFlight = new();

    private readonly IDistributedCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;
    private readonly TimeProvider _timeProvider;

    public CachingBehavior(IDistributedCache cache, ICurrentUser currentUser, ILogger<CachingBehavior<TRequest, TResponse>> logger, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _cache = cache;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next();

        // Fix the expiry before any I/O, so a slow write cannot push a value past the generation's lifetime.
        if (cacheable.CacheDuration > CacheTombstone.Ttl)
            return await next();

        // No signed-in user means no owner scope. Skip the cache rather than share one global key.
        if (cacheable is IOwnerScopedRequest && !_currentUser.IsAuthenticated)
            return await next();

        var expiresAtUtc = _timeProvider.GetUtcNow() + cacheable.CacheDuration;
        var cacheKey = ResolveCacheKey(cacheable);
        var cached = (await TryGetAsync(cacheKey, cancellationToken)).Value;
        var (generationKnown, generation) = await TryGetAsync(CacheTombstone.KeyFor(cacheKey), cancellationToken);
        // Older replicas wrote "1" as the tombstone. That cannot tell one invalidation from the next, so treat it as unknown.
        generationKnown &= generation != "1";
        if (cached is not null)
        {
            var envelope = TryDeserializeEnvelope(cached);
            if (envelope is not null && generationKnown && envelope.Generation == generation &&
                _timeProvider.GetUtcNow() < envelope.ExpiresAtUtc)
            {
                if (cacheable.CacheRefreshWindow <= TimeSpan.Zero || _timeProvider.GetUtcNow() < envelope.RefreshAfterUtc)
                {
                    _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
                    return envelope.Value!;
                }

                // One caller recomputes the value under its own identity. The rest keep the cached value, which is still valid.
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
                catch (Exception ex) when (ex is not OperationCanceledException && _timeProvider.GetUtcNow() < envelope.ExpiresAtUtc)
                {
                    // The cached value has not expired yet. Only the early refresh gets this fallback.
                    _logger.LogWarning(ex, "Refresh-ahead recompute failed for {CacheKey}; serving the cached value until the next attempt", cacheKey);
                    return envelope.Value!;
                }
                finally
                {
                    RefreshesInFlight.TryRemove(cacheKey, out _);
                }
            }

            // An out-of-date generation is a normal miss. Content that cannot be read is not. Log them differently.
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

    // Fails open. If Redis is down the query reads the database instead of returning a 500. Cancellation still goes through.
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
        if (!known || currentGeneration != generation || _timeProvider.GetUtcNow() >= expiresAtUtc)
            return;

        // Correctness comes from storing the generation read before the handler ran. This check only saves pointless writes.
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
