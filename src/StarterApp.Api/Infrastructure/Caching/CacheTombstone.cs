namespace StarterApp.Api.Infrastructure.Caching;

internal static class CacheTombstone
{
    // The :inv key holds a unique generation, not a flag. It must live longer than any value fetched before the change.
    internal static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    internal static string KeyFor(string cacheKey) => cacheKey + ":inv";
}
