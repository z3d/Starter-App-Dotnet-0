using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Probes the payload archive with a container-existence read: the cheapest call that proves
// authenticated connectivity *with the permission the store actually uses*. A read of the blob
// service's own properties would need blobServices/read, which Storage Blob Data Contributor —
// the role a deployed identity holds — does not carry, so it reported a working archive as
// unhealthy. Takes the process-wide client through the provider that AddPayloadCapture registers
// (the archive store uses the same one), and is itself a DI singleton, so a probe never
// constructs a client or a DefaultAzureCredential of its own.
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
            // The container is created on first write, so "does not exist yet" is still a healthy,
            // authenticated answer; only a failed call is unhealthy.
            await _container.ExistsAsync(cancellationToken);
            return HealthCheckResult.Healthy("Payload archive storage is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Payload archive storage is unreachable", ex);
        }
    }
}
