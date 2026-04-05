using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Caching;

public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;

    public CachingBehavior(IDistributedCache cache, ILogger<CachingBehavior<TRequest, TResponse>> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next();

        var cached = await _cache.GetStringAsync(cacheable.CacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit for {CacheKey}", cacheable.CacheKey);
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
            await _cache.SetStringAsync(cacheable.CacheKey, serialized, options, cancellationToken);
        }

        return result;
    }
}
