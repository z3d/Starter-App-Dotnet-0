using System.Diagnostics;

namespace StarterApp.Api.Endpoints.Filters;

/// <summary>
/// Endpoint filter that logs request execution time and details.
/// </summary>
public class LoggingFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var endpoint = httpContext.GetEndpoint()?.DisplayName ?? "Unknown";
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path;

        Log.Information("Executing {Method} {Path} [{Endpoint}]", method, path, endpoint);

        var stopwatch = Stopwatch.StartNew();
        var result = await next(context);
        stopwatch.Stop();

        Log.Information("Completed {Method} {Path} in {ElapsedMs}ms", method, path, stopwatch.ElapsedMilliseconds);

        return result;
    }
}
