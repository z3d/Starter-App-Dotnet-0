using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace StarterApp.Api.Infrastructure.Caching;

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
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

        var cacheKey = ResolveCacheKey(cacheable);
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", cacheKey);
            return JsonSerializer.Deserialize<TResponse>(cached)!;
        }

        var result = await next();

        if (result is not null)
        {
            var serialized = JsonSerializer.Serialize(result);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = cacheable.CacheDuration
            };
            await _cache.SetStringAsync(cacheKey, serialized, options, cancellationToken);
        }

        return result;
    }

    private string ResolveCacheKey(ICacheable cacheable)
    {
        return cacheable is IOwnerScopedRequest && _currentUser.IsAuthenticated
            ? OwnerScopedCacheKey.Create(cacheable.CacheKey, _currentUser)
            : cacheable.CacheKey;
    }
}
