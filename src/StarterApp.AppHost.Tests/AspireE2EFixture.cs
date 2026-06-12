using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace StarterApp.AppHost.Tests;

// One distributed-app boot for the whole E2E collection. Booting per test multiplied the
// documented Service Bus emulator flake surface by the number of facts (the watch-item in
// docs/ARCHITECTURE_REVIEW.md) and paid ~4 cold starts of pure overhead; isolation between
// facts is already structural — every test correlates by its own ids (GUID emails, order ids,
// correlation ids), never by global state.
public sealed class AspireE2EFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        App = await appHost.BuildAsync();
        await App.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }

    // No identity headers: orchestrator probes (Docker, Kubernetes) carry none, so health
    // endpoints must be verifiable anonymously — an authenticated-only client would mask a
    // regression that accidentally puts gateway identity in front of readiness/liveness.
    public HttpClient CreateAnonymousApiClient() => App.CreateHttpClient("api");

    public HttpClient CreateApiClient()
    {
        var client = CreateAnonymousApiClient();
        client.DefaultRequestHeaders.Add("X-Authenticated-Subject", "apphost-test-user");
        client.DefaultRequestHeaders.Add("X-Authenticated-Principal-Type", "User");
        client.DefaultRequestHeaders.Add("X-Authenticated-Tenant-Id", "apphost-test-tenant");
        client.DefaultRequestHeaders.Add("X-Authenticated-Scopes", "customers:read customers:write orders:read orders:write products:read products:write");
        client.DefaultRequestHeaders.Add("X-Authenticated-Amr", "mfa pwd");
        return client;
    }
}

[CollectionDefinition("Aspire E2E")]
public sealed class AspireE2ECollection : ICollectionFixture<AspireE2EFixture>;
