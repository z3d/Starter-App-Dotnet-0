using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StarterApp.Api.Infrastructure.HealthChecks;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.HealthChecks;

public class PayloadArchiveHealthCheckRegistrationTests
{
    [Fact]
    public void PayloadArchiveHealthCheck_IsASingletonSharingTheProcessWideBlobClient()
    {
        // AddCheck<T> alone activates a new instance per health run, and the old constructor built
        // a BlobServiceClient + DefaultAzureCredential each time — a credential-chain walk and an
        // IMDS token fetch per probe on two unthrottled routes. The check and the client must both
        // resolve as singletons, and the registration HealthCheckService uses must reuse them.
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PayloadCapture:ConnectionString"] = "UseDevelopmentStorage=true"
        });
        builder.Services.AddLogging();
        builder.AddPayloadCapture();
        builder.Services.AddApiHealthChecks(builder.Configuration);

        using var host = builder.Build();
        var provider = host.Services;

        var check = provider.GetRequiredService<PayloadArchiveHealthCheck>();
        Assert.Same(check, provider.GetRequiredService<PayloadArchiveHealthCheck>());

        var client = provider.GetRequiredService<BlobServiceClient>();
        Assert.Same(client, provider.GetRequiredService<BlobServiceClient>());
        Assert.IsType<AzureBlobPayloadArchiveStore>(provider.GetRequiredService<IPayloadArchiveStore>());

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
            .Registrations.Single(r => r.Name == "payload-archive");
        Assert.Same(check, registration.Factory(provider));
    }

    [Fact]
    public void PayloadArchiveHealthCheck_IsNotRegistered_WhenNoArchiveIsConfigured()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Services.AddLogging();
        builder.AddPayloadCapture();
        builder.Services.AddApiHealthChecks(builder.Configuration);

        using var host = builder.Build();

        Assert.Null(host.Services.GetService<BlobServiceClient>());
        Assert.Null(host.Services.GetService<PayloadArchiveHealthCheck>());
        Assert.IsType<NullPayloadArchiveStore>(host.Services.GetRequiredService<IPayloadArchiveStore>());
        Assert.DoesNotContain(
            host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            r => r.Name == "payload-archive");
    }
}
