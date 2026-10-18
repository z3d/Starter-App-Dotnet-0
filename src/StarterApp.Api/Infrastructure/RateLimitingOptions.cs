using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure;

// Bound from the RateLimiting section and validated at start-up. The k6 performance gate raises PermitLimit because all of its load comes from one caller.
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;

    // A queue of zero rejected the bursts that the integration tests and real clients send. Keep this in step with appsettings.json.
    [Range(0, 10_000)]
    public int QueueLimit { get; set; } = 5;
}
