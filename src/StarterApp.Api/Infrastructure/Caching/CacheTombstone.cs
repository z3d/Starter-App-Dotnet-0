namespace StarterApp.Api.Infrastructure.Caching;

internal static class CacheTombstone
{
    // Retain the :inv key for compatibility with earlier replicas. Its value is now a unique
    // invalidation generation, not a constant flag. It must outlive any value fetched before
    // the mutation: readers pin their own absolute expiry before starting the database read.
    // Longer cache durations bypass caching, and conventions flag them for deliberate review.
    internal static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    internal static string KeyFor(string cacheKey) => cacheKey + ":inv";
}
