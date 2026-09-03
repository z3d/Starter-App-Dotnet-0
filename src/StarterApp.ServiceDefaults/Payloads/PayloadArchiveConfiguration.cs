using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace StarterApp.ServiceDefaults.Payloads;

// Single owner of "is a payload archive configured, and with which client?" The bound options
// are the source of truth at resolution time (so Configure/PostConfigure<PayloadCaptureOptions>
// is honoured); the raw-configuration probe exists only for registration-time decisions.
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

    public static bool IsConfigured(PayloadCaptureOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        return ResolveConnectionString(options, configuration) is not null ||
            !string.IsNullOrWhiteSpace(options.AccountUri);
    }

    public static BlobServiceClient? CreateClient(PayloadCaptureOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = ResolveConnectionString(options, configuration);
        if (connectionString is not null)
            return new BlobServiceClient(connectionString);

        return string.IsNullOrWhiteSpace(options.AccountUri)
            ? null
            : new BlobServiceClient(new Uri(options.AccountUri), new DefaultAzureCredential());
    }

    private static string? ResolveConnectionString(PayloadCaptureOptions options, IConfiguration configuration)
    {
        var connectionString = options.ConnectionString
            ?? configuration.GetConnectionString("payloadarchive")
            ?? configuration.GetConnectionString("payloadstorage");
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }
}

// The one BlobServiceClient the process holds for the payload archive, or null when none is
// configured. A dedicated wrapper rather than a bare BlobServiceClient registration so an
// unrelated Azure client added to DI (e.g. Aspire's AddAzureBlobClient for another account)
// can never be picked up by the archive store or its health check.
public sealed class PayloadArchiveClientProvider
{
    public PayloadArchiveClientProvider(BlobServiceClient? client)
    {
        Client = client;
    }

    public BlobServiceClient? Client { get; }
}
