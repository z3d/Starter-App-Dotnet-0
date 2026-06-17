using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Round-trips a probe key through IDistributedCache — Redis when orchestrated, the in-memory
// fallback in standalone dev/tests (where the round-trip is trivially healthy).
public sealed class DistributedCacheHealthCheck : IHealthCheck
{
    private const string ProbeKey = "health-probe:distributed-cache";
    private static readonly byte[] ProbeValue = [1];

    private readonly IDistributedCache _cache;

    public DistributedCacheHealthCheck(IDistributedCache cache)
    {
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _cache.SetAsync(
                ProbeKey,
                ProbeValue,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) },
                cancellationToken);

            var roundTripped = await _cache.GetAsync(ProbeKey, cancellationToken);
            return roundTripped is not null
                ? HealthCheckResult.Healthy("Distributed cache round-trip succeeded")
                : HealthCheckResult.Unhealthy("Distributed cache wrote but did not read back the probe key");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Distributed cache is unreachable", ex);
        }
    }
}
