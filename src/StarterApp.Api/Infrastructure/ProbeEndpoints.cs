using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StarterApp.Api.Infrastructure;

// Every anonymous endpoint, in one place. Probes also skip the rate limiter: kubelet probes from the node IP, and a 429 could restart a healthy pod.
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
