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
        app.MapGet("/liveness", (TimeProvider timeProvider) => Results.Ok(new
        {
            status = "alive",
            timestampUtc = timeProvider.GetUtcNow(),
        }));

        app.MapHealthChecks("/healthiness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("durable"),
            ResponseWriter = WriteHealthinessResponseAsync,
        });

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
