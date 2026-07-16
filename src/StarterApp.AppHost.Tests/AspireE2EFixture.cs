using System.Diagnostics;
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

    // StartAsync returns before the slow resources are usable. The fixture gates every fact on
    // the API's readiness probe (which transitively proves the migrator and the Service Bus
    // emulator — the documented flake source). The Functions container is deliberately NOT part
    // of that gate: its image is rebuilt in-container whenever the source changes, which can
    // dwarf every other boot cost, and most facts never touch it. Facts that do consume the
    // subscriber container must await EnsureFunctionsReadyAsync() instead.
    private static readonly TimeSpan ApiBootTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FunctionsBootTimeout = TimeSpan.FromMinutes(10);

    private Task? _functionsReady;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.StarterApp_AppHost>();
        App = await appHost.BuildAsync();
        await App.StartAsync();

        using var apiClient = CreateAnonymousApiClient();
        await WaitForHttpOkAsync(apiClient, "/health/ready", "api", ApiBootTimeout);
    }

    // Lazy and cached: only the facts that depend on the subscriber container pay for its
    // in-container image build + boot, and they pay once per collection.
    public Task EnsureFunctionsReadyAsync() => _functionsReady ??= WaitForFunctionsAsync();

    private async Task WaitForFunctionsAsync()
    {
        using var functionsClient = App.CreateHttpClient("functions");
        await WaitForHttpOkAsync(functionsClient, "/", "functions", FunctionsBootTimeout);
    }

    private static async Task WaitForHttpOkAsync(HttpClient client, string path, string resourceName, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        string lastOutcome = "no response yet";

        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await client.GetAsync(path, attempt.Token);
                if (response.IsSuccessStatusCode)
                    return;

                lastOutcome = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                lastOutcome = ex.GetType().Name;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException(
            $"Resource '{resourceName}' did not answer 2xx on '{path}' within {timeout} (last outcome: {lastOutcome}).");
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
