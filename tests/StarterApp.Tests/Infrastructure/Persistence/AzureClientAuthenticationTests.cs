using StarterApp.ServiceDefaults;

namespace StarterApp.Tests.Infrastructure.Persistence;

public class AzureClientAuthenticationTests
{
    // Synthetic values only.
    [Theory]
    [InlineData("https://acct.blob.core.windows.net/", true)]
    [InlineData("https://ns.servicebus.windows.net:443/", true)]
    [InlineData("ns.servicebus.windows.net", true)]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/", true)]
    [InlineData("Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;", false)]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=a2V5;EndpointSuffix=core.windows.net", false)]
    [InlineData("BlobEndpoint=https://acct.blob.core.windows.net/;SharedAccessSignature=sv=2024", false)]
    [InlineData("UseDevelopmentStorage=true", false)]
    [InlineData("localhost:6379,password=dev-only", false)]
    [InlineData("cache.australiaeast.redis.azure.net:10000,ssl=true", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void UsesManagedIdentity_OnlyForAValueWithNoKey(string? value, bool expected)
    {
        Assert.Equal(expected, AzureClientAuthentication.UsesManagedIdentity(value));
    }

    [Theory]
    [InlineData("https://acct.blob.core.windows.net/", "acct.blob.core.windows.net")]
    [InlineData("https://ns.servicebus.windows.net:443/", "ns.servicebus.windows.net")]
    [InlineData("ns.servicebus.windows.net", "ns.servicebus.windows.net")]
    [InlineData("Endpoint=sb://ns.servicebus.windows.net/;", "ns.servicebus.windows.net")]
    public void Endpoint_ExtractsTheHost_FromEveryShapeAspireEmits(string value, string expectedHost)
    {
        Assert.Equal(expectedHost, AzureClientAuthentication.Endpoint(value).Host);
    }

    [Fact]
    public async Task CreateClients_WithAnEndpoint_UseTheCredential_AndTheHost()
    {
        var credential = new StaticCredential();

        await using var serviceBus = AzureClientAuthentication.CreateServiceBusClient("https://ns.servicebus.windows.net:443/", credential);
        var blobs = AzureClientAuthentication.CreateBlobServiceClient("https://acct.blob.core.windows.net/", credential);

        Assert.Equal("ns.servicebus.windows.net", serviceBus.FullyQualifiedNamespace);
        Assert.Equal("acct", blobs.AccountName);
    }

    [Fact]
    public async Task CreateClients_WithAKeyedConnectionString_IgnoreTheCredential()
    {
        var credential = new StaticCredential();

        await using var serviceBus = AzureClientAuthentication.CreateServiceBusClient(
            "Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dmFsdWU=", credential);
        var blobs = AzureClientAuthentication.CreateBlobServiceClient("UseDevelopmentStorage=true", credential);

        Assert.Equal("ns.servicebus.windows.net", serviceBus.FullyQualifiedNamespace);
        Assert.Equal("devstoreaccount1", blobs.AccountName);
    }

    [Theory]
    [InlineData("localhost:6379,password=dev-only")]
    [InlineData("cache.australiaeast.redis.azure.net:10000,ssl=true,password=stillakey")]
    [InlineData("localhost:6379")]
    public void ConfigureRedis_LeavesLocalOrKeyedOptionsAlone(string connectionString)
    {
        var options = StackExchange.Redis.ConfigurationOptions.Parse(connectionString);
        var before = options.ToString(includePassword: true);

        AzureClientAuthentication.ConfigureRedis(options, new StaticCredential());

        Assert.Equal(before, options.ToString(includePassword: true));
    }

    [Theory]
    [InlineData("cache.australiaeast.redis.azure.net", true)]
    [InlineData("cache.redis.cache.windows.net", true)]
    [InlineData("localhost", false)]
    [InlineData("redis", false)]
    public void IsAzureRedisHost_RecognisesTheAzureSuffixes(string host, bool expected)
    {
        Assert.Equal(expected, AzureClientAuthentication.IsAzureRedisHost(new System.Net.DnsEndPoint(host, 10000)));
    }

    private sealed class StaticCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.MaxValue);

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
