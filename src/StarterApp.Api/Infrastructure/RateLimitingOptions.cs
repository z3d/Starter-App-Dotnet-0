using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure;

// Per-partition fixed-window limits for the global rate limiter (partition key:
// verified tenant/subject for authenticated traffic, client IP otherwise). Bound from
// the RateLimiting section and validated at startup so a deployment tunes limits
// without a code change — the k6 perf gate relies on overriding PermitLimit because
// its entire load runs under a single caller identity.
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    // A small queue smooths a burst that briefly exceeds PermitLimit: queued requests wait for the
    // next window instead of getting a 429. Validated 2026-09-03 as deliberate — setting it to 0
    // rejected bursts the integration suite (and real clients) legitimately produce. Rejected
    // requests carry Retry-After. Keep this in step with appsettings.json.
    [Range(0, 10_000)]
    public int QueueLimit { get; set; } = 5;
}
