using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace StarterApp.Functions;

// The worker's one HTTP address, for a readiness probe: 200 when every check passes, 503 otherwise, each check listed by name. It does no work for a caller.
public sealed class HealthFunction
{
    public const string Route = "health";

    private readonly WorkerHealth _health;
    private readonly ILogger<HealthFunction> _logger;

    public HealthFunction(WorkerHealth health, ILogger<HealthFunction> logger)
    {
        _health = health;
        _logger = logger;
    }

    [Function(nameof(HealthFunction))]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Route)] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checks = await _health.CheckAsync(cancellationToken);
        var healthy = WorkerHealth.IsHealthy(checks);
        if (!healthy)
            _logger.LogError("Worker health check failed: {Checks}", string.Join("; ", checks.Select(check => $"{check.Key}: {check.Value}")));

        var response = request.CreateResponse();
        await response.WriteAsJsonAsync(checks, cancellationToken);
        // WriteAsJsonAsync sets 200, so the status goes on afterwards.
        response.StatusCode = healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        return response;
    }
}
