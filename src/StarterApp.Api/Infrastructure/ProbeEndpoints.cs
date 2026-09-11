using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StarterApp.Api.Infrastructure;

// Every anonymous endpoint in the API, in one place:
//   /health, /health/ready, /health/live, /alive — the Aspire and Kubernetes probes.
//   /liveness    — answers from the process alone, evaluates no dependencies.
//   /healthiness — deep probe of the durable (deployable) backing resources: every health check
//                  tagged "durable" (database, distributed cache, Service Bus, payload archive
//                  where configured) with per-check detail; 503 when any check is unhealthy.
// Probes carry no bearer token, so each one opts out of the fallback authorization policy with
// AllowAnonymous. ApiConventionTests checks that nothing else does.
// They also opt out of the global rate limiter: the limiter buckets anonymous traffic by client
// IP, and under Kubernetes the kubelet probes from the node IP, so a 429 on a probe could
// restart an otherwise healthy pod.
public static class ProbeEndpoints
{
    public static IEndpointRouteBuilder MapProbeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health").AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        }).AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        }).AllowAnonymous().DisableRateLimiting();
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        }).AllowAnonymous().DisableRateLimiting();

        app.MapGet("/liveness", (TimeProvider timeProvider) => Results.Ok(new
        {
            status = "alive",
            timestampUtc = timeProvider.GetUtcNow(),
        })).AllowAnonymous().DisableRateLimiting();

        app.MapHealthChecks("/healthiness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("durable"),
            ResponseWriter = WriteHealthinessResponseAsync,
        }).AllowAnonymous().DisableRateLimiting();

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
