using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure;

// Bound from RateLimiting and validated at startup; the k6 perf gate overrides PermitLimit because its load runs as one caller.
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    // Zero rejected bursts the integration suite and real clients produce. Keep in step with appsettings.json.
    [Range(0, 10_000)]
    public int QueueLimit { get; set; } = 5;
}
