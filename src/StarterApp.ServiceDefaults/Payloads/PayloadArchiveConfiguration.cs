using Microsoft.Extensions.Configuration;

namespace StarterApp.ServiceDefaults.Payloads;

// Single answer to "is a payload archive configured?" for every registration that depends on it
// (the shared BlobServiceClient, the archive store, the API's payload-archive health check), so
// the connection-resolution order cannot drift between them.
public static class PayloadArchiveConfiguration
{
    public static bool IsConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !string.IsNullOrWhiteSpace(configuration["PayloadCapture:ConnectionString"]) ||
            !string.IsNullOrWhiteSpace(configuration["PayloadCapture:AccountUri"]) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("payloadarchive")) ||
            !string.IsNullOrWhiteSpace(configuration.GetConnectionString("payloadstorage"));
    }

    public static BlobServiceClientSource ResolveClientSource(PayloadCaptureOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = options.ConnectionString
            ?? configuration.GetConnectionString("payloadarchive")
            ?? configuration.GetConnectionString("payloadstorage");

        return new BlobServiceClientSource(
            string.IsNullOrWhiteSpace(connectionString) ? null : connectionString,
            string.IsNullOrWhiteSpace(options.AccountUri) ? null : new Uri(options.AccountUri));
    }
}

public sealed record BlobServiceClientSource(string? ConnectionString, Uri? AccountUri)
{
    public bool IsConfigured => ConnectionString is not null || AccountUri is not null;
}
