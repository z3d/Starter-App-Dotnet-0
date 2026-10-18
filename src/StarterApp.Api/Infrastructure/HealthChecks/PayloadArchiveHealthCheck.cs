using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Checks that the container exists, not the service properties. Storage Blob Data Contributor cannot read service properties, and that check reported a working archive as unhealthy.
public sealed class PayloadArchiveHealthCheck : IHealthCheck
{
    private readonly BlobContainerClient? _container;

    public PayloadArchiveHealthCheck(PayloadArchiveClientProvider clientProvider, IOptions<PayloadCaptureOptions> options)
    {
        ArgumentNullException.ThrowIfNull(clientProvider);
        ArgumentNullException.ThrowIfNull(options);
        _container = clientProvider.Client?.GetBlobContainerClient(options.Value.ContainerName);
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_container is null)
            return HealthCheckResult.Unhealthy("Payload archive storage is not configured");

        try
        {
            // The container is created on the first write, so "does not exist yet" is still healthy.
            await _container.ExistsAsync(cancellationToken);
            return HealthCheckResult.Healthy("Payload archive storage is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Payload archive storage is unreachable", ex);
        }
    }
}
