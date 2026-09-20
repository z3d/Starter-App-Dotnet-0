using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using StackExchange.Redis;

namespace StarterApp.ServiceDefaults;

// The Azure-client counterpart of DatabaseAuthentication: the shape of the configured value
// decides the credential. A connection string that carries a key or password (AccountKey,
// SharedAccessKey, SharedAccessSignature, password=, or the emulator's UseDevelopmentStorage) is
// used as given; a bare endpoint, namespace or Redis host means "connect as the hosting identity"
// and takes a DefaultAzureCredential. Aspire injects the keyed form for the local emulators and
// containers and the bare form for the deployed Azure resources, so the same configuration key
// works in both places.
public static class AzureClientAuthentication
{
    public static bool UsesManagedIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return !value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("SharedAccessKey=", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("password=", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("UseDevelopmentStorage=", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("UseDevelopmentEmulator=", StringComparison.OrdinalIgnoreCase);
    }

    public static string Describe(string? value) =>
        UsesManagedIdentity(value) ? "managed identity (Entra token)" : "connection string";

    public static BlobServiceClient CreateBlobServiceClient(string value, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return UsesManagedIdentity(value)
            ? new BlobServiceClient(Endpoint(value), credential ?? new DefaultAzureCredential())
            : new BlobServiceClient(value);
    }

    public static ServiceBusClient CreateServiceBusClient(string value, TokenCredential? credential = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return UsesManagedIdentity(value)
            ? new ServiceBusClient(Endpoint(value).Host, credential ?? new DefaultAzureCredential())
            : new ServiceBusClient(value);
    }

    // Azure Managed Redis / Azure Cache for Redis with access keys disabled: the StackExchange.Redis
    // options Aspire builds from the connection string get Microsoft's Entra token exchange
    // (Microsoft.Azure.StackExchangeRedis), which sets the user from the token and refreshes it.
    // A local container's "host:port,password=…" is left alone. Called from the client
    // registration's configureOptions hook, which is synchronous, hence the blocking wait at start-up.
    public static void ConfigureRedis(ConfigurationOptions options, TokenCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrEmpty(options.Password) || !options.EndPoints.Any(IsAzureRedisHost))
            return;

        options.ConfigureForAzureWithTokenCredentialAsync(credential ?? new DefaultAzureCredential()).GetAwaiter().GetResult();
    }

    internal static bool IsAzureRedisHost(System.Net.EndPoint endPoint) =>
        endPoint is System.Net.DnsEndPoint dns &&
        (dns.Host.EndsWith(".redis.azure.net", StringComparison.OrdinalIgnoreCase) ||
         dns.Host.EndsWith(".redis.cache.windows.net", StringComparison.OrdinalIgnoreCase));

    // Accepts "https://account.blob.core.windows.net", "Endpoint=sb://ns.servicebus.windows.net/"
    // or a bare "ns.servicebus.windows.net".
    internal static Uri Endpoint(string value)
    {
        var endpoint = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase) ? part["Endpoint=".Length..] : part)
            .First();

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
            return uri;

        if (Uri.TryCreate($"https://{endpoint}", UriKind.Absolute, out uri) && uri.Host.Length > 0)
            return uri;

        throw new InvalidOperationException($"'{value}' is neither a connection string nor an endpoint.");
    }
}
