using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StarterApp.Api.Infrastructure;

// Probe endpoints for external monitors (APIM, container platforms, uptime checks):
//   /liveness    — answers from the process alone, evaluates no dependencies.
//   /healthiness — deep probe of the durable (deployable) backing resources: every health check
//                  tagged "durable" (database, distributed cache, Service Bus, payload archive
//                  where configured) with per-check detail; 503 when any check is unhealthy.
// Deliberately outside /api/v1 so they sit with /health* ahead of the gateway-identity
// contract — probes carry no caller identity.
public static class ProbeEndpoints
{
    public static WebApplication MapProbeEndpoints(this WebApplication app)
    {
        // These external-monitor probes opt out of the global rate limiter for the same reason as
        // the /health* set in Program.cs: they are unauthenticated and would otherwise share an
        // IP-keyed partition, letting throttling restart/evict a healthy instance.
        app.MapGet("/liveness", (TimeProvider timeProvider) => Results.Ok(new
        {
            status = "alive",
            timestampUtc = timeProvider.GetUtcNow(),
        })).DisableRateLimiting();

        app.MapHealthChecks("/healthiness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("durable"),
            ResponseWriter = WriteHealthinessResponseAsync,
        }).DisableRateLimiting();

        return app;
    }

    private static Task WriteHealthinessResponseAsync(HttpContext context, HealthReport report)
    {
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = (long)entry.Value.Duration.TotalMilliseconds,
            }),
        });
    }
}
