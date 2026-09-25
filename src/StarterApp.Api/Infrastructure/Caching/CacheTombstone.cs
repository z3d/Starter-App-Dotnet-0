namespace StarterApp.Api.Infrastructure.Caching;

internal static class CacheTombstone
{
    // The :inv key now holds a unique generation, not a flag; it must outlive any value fetched before the mutation.
    internal static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    internal static string KeyFor(string cacheKey) => cacheKey + ":inv";
}
