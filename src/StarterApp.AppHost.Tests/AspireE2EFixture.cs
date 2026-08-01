using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
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

    // No bearer token: orchestrator probes (Docker, Kubernetes) carry none, so health
    // endpoints must be verifiable anonymously — an authenticated-only client would mask a
    // regression that accidentally puts authentication in front of readiness/liveness.
    public HttpClient CreateAnonymousApiClient() => App.CreateHttpClient("api");

    // Authenticated client: every request rides a real access token minted by the dev Keycloak
    // realm (client-credentials grant against the committed starterapp-dev client), so E2E facts
    // exercise the same discovery -> JWKS -> asymmetric-verify path as production. The handler
    // caches the token and re-mints near expiry because a full E2E run can outlive one token.
    public HttpClient CreateApiClient()
    {
        // Target the https endpoint directly with redirect-following OFF: HttpClientHandler
        // strips the Authorization header when it follows a redirect, so riding the
        // UseHttpsRedirection 307 from the http endpoint silently de-authenticates every
        // request (the old header-identity model survived redirects; bearer tokens don't).
        // The dev certificate is accepted because this client only ever talks to the local rig.
        return new HttpClient(new KeycloakTokenHandler(App)
        {
            InnerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            }
        })
        {
            BaseAddress = App.GetEndpoint("api", "https")
        };
    }

    private sealed class KeycloakTokenHandler(DistributedApplication app) : DelegatingHandler
    {
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private string? _token;
        private DateTimeOffset _refreshAfter = DateTimeOffset.MinValue;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(cancellationToken));
            var response = await base.SendAsync(request, cancellationToken);

            // A 401 here means the API rejected a token this fixture just minted — fail with the
            // bearer handler's reason (WWW-Authenticate) instead of a bare status code.
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new InvalidOperationException(
                    $"API rejected a fixture-minted token: {string.Join(" | ", response.Headers.WwwAuthenticate)}");

            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _refreshLock.Dispose();
            base.Dispose(disposing);
        }

        private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (_token is not null && DateTimeOffset.UtcNow < _refreshAfter)
                return _token;

            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (_token is not null && DateTimeOffset.UtcNow < _refreshAfter)
                    return _token;

                using var keycloak = app.CreateHttpClient("keycloak", "http");
                using var response = await keycloak.PostAsync(
                    "/realms/starterapp/protocol/openid-connect/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = "starterapp-dev",
                        ["client_secret"] = "local-dev-client-secret-not-a-secret",
                        // The resource scopes are optional client scopes so callers (and the demo
                        // walkthrough) choose what a token carries; request the full set here.
                        ["scope"] = "customers:read customers:write orders:read orders:write products:read products:write"
                    }),
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                _token = json.RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("Keycloak token response carried no access_token.");
                var expiresInSeconds = json.RootElement.GetProperty("expires_in").GetInt32();
                _refreshAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresInSeconds - 60, 30));
                return _token;
            }
            finally
            {
                _refreshLock.Release();
            }
        }
    }
}

[CollectionDefinition("Aspire E2E")]
public sealed class AspireE2ECollection : ICollectionFixture<AspireE2EFixture>;
