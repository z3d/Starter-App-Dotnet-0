namespace StarterApp.Api.Infrastructure.Caching;

internal static class CacheTombstone
{
    // A mutation writes a short-lived tombstone alongside removing the cached entry. A read that
    // missed the cache before the write committed checks this tombstone immediately before it
    // repopulates the key; if present, it skips the write so a stale value cannot re-poison the
    // just-invalidated key for the full cache TTL. The lifetime only needs to comfortably exceed
    // the worst-case by-id read latency (DB fetch + serialize), not the cache entry TTL.
    internal static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    internal static string KeyFor(string cacheKey) => cacheKey + ":inv";
}
