using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Probes the payload archive blob account with a service-properties read — the cheapest call that
// proves authenticated connectivity. Takes the process-wide client through the provider that
// AddPayloadCapture registers (the archive store uses the same one), and is itself a DI singleton,
// so a probe never constructs a client or a DefaultAzureCredential of its own.
public sealed class PayloadArchiveHealthCheck : IHealthCheck
{
    private readonly BlobServiceClient? _client;

    public PayloadArchiveHealthCheck(PayloadArchiveClientProvider clientProvider)
    {
        ArgumentNullException.ThrowIfNull(clientProvider);
        _client = clientProvider.Client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return HealthCheckResult.Unhealthy("Payload archive storage is not configured");

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
