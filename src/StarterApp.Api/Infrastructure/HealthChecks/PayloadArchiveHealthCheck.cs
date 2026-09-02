using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Probes the payload archive blob account with a service-properties read — the cheapest call that
// proves authenticated connectivity. Takes the process-wide BlobServiceClient that AddPayloadCapture
// registers (and the archive store uses), and is itself registered as a singleton, so a probe
// never constructs a client or a DefaultAzureCredential of its own. The check is only registered
// when the archive is configured, which is also when the client is.
public sealed class PayloadArchiveHealthCheck : IHealthCheck
{
    private readonly BlobServiceClient _client;

    public PayloadArchiveHealthCheck(BlobServiceClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy("Payload archive storage is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Payload archive storage is unreachable", ex);
        }
    }
}
