namespace StarterApp.Api.Infrastructure.Caching;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }

    // Stampede protection: in the final CacheRefreshWindow of CacheDuration, one request
    // recomputes inline (correct gateway identity — never a background scope) while
    // concurrent requests keep the cached value. Must be positive and smaller than
    // CacheDuration (convention-tested).
    TimeSpan CacheRefreshWindow { get; }
}
