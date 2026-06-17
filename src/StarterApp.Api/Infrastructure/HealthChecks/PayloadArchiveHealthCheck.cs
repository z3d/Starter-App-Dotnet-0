using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Api.Infrastructure.HealthChecks;

// Probes the payload archive blob account with a service-properties read — the cheapest call that
// proves authenticated connectivity. Connection resolution mirrors AddPayloadCapture's store
// factory (options first, then the Aspire-injected connection strings); the check is only
// registered when one of those sources is configured.
public sealed class PayloadArchiveHealthCheck : IHealthCheck
{
    private readonly BlobServiceClient _client;

    public PayloadArchiveHealthCheck(IConfiguration configuration, IOptions<PayloadCaptureOptions> options)
    {
        var connectionString = options.Value.ConnectionString
            ?? configuration.GetConnectionString("payloadarchive")
            ?? configuration.GetConnectionString("payloadstorage");

        _client = !string.IsNullOrWhiteSpace(connectionString)
            ? new BlobServiceClient(connectionString)
            : new BlobServiceClient(new Uri(options.Value.AccountUri!), new DefaultAzureCredential());
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
