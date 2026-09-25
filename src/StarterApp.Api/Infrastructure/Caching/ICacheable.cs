namespace StarterApp.Api.Infrastructure.Caching;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }

    // In this final window one request recomputes inline while the rest keep the cached value. Positive and smaller than CacheDuration.
    TimeSpan CacheRefreshWindow { get; }
}
