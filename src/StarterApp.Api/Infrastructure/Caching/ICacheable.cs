namespace StarterApp.Api.Infrastructure.Caching;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
}
