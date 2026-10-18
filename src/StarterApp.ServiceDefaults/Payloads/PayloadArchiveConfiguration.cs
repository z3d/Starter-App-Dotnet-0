using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace StarterApp.ServiceDefaults.Payloads;

// The bound options are the truth once services resolve. This raw check of configuration is only for decisions made while registering services.
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
            return AzureClientAuthentication.CreateBlobServiceClient(connectionString);

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

// A wrapper, so the archive store can never pick up an unrelated BlobServiceClient from DI.
public sealed class PayloadArchiveClientProvider
{
    public PayloadArchiveClientProvider(BlobServiceClient? client)
    {
        Client = client;
    }

    public BlobServiceClient? Client { get; }
}
