namespace StarterApp.Api.Infrastructure.Caching;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }

    // During this last part of the cache duration, one request recomputes the value while the others keep the cached one. Must be positive and shorter than CacheDuration.
    TimeSpan CacheRefreshWindow { get; }
}
