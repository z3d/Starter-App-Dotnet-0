using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using StarterApp.Application.Infrastructure.Identity.Session;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class BrowserSessionTests
{
    private const string WebOrigin = "https://web.example.test";
    private const string ProviderOrigin = "https://stub-idp.example.test";
    private const string ClientSecret = "session-test-secret";

    private readonly ApiTestFixture _fixture;

    public BrowserSessionTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SignIn_SendsThePersonToTheProvider_WithPkceAndState_AndBindsTheBrowser()
    {
        await using var rig = new Rig(_fixture);

        var response = await rig.Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?returnTo=/orders");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith($"{ProviderOrigin}/authorize?", location.ToString(), StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("starterapp-session", query["client_id"]);
        Assert.Equal($"{WebOrigin}{BrowserSessionEndpoints.CallbackPath}", query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(BrowserSessionCookies.SignIn, StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignedIn_TheBrowserHoldsOnlyAStrictHttpOnlyCookie_AndTheApiAnswersIt()
    {
        await using var rig = new Rig(_fixture);

        var callback = await rig.SignInAsync("/orders?page=2");

        Assert.Equal("/orders?page=2", callback.Headers.Location!.ToString());
        var cookie = Assert.Single(callback.Headers.GetValues("Set-Cookie"), value => value.StartsWith(BrowserSessionCookies.Session, StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);

        var exchange = Assert.Single(rig.Provider.Requests, request => request["grant_type"] == "authorization_code");
        Assert.Equal("the-code", exchange["code"]);
        Assert.False(string.IsNullOrEmpty(exchange["code_verifier"]));
        Assert.Equal($"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"starterapp-session:{ClientSecret}"))}", rig.Provider.Authorizations.Single());

        Assert.Equal(HttpStatusCode.OK, (await rig.Client.GetAsync("/api/v1/products")).StatusCode);

        var sessionResponse = await rig.Client.GetAsync(BrowserSessionEndpoints.SessionPath);
        var body = await sessionResponse.Content.ReadAsStringAsync();
        var session = JsonSerializer.Deserialize<BrowserSessionResponse>(body, JsonSerializerOptions.Web)!;
        Assert.Equal(TestJwtIdentity.DefaultSubject, session.Subject);
        Assert.Equal(TestJwtIdentity.DefaultTenantId, session.TenantId);
        Assert.Contains("mfa", session.AuthenticationMethods);
        Assert.False(string.IsNullOrEmpty(session.CsrfToken));
        Assert.DoesNotContain(rig.Provider.LastAccessToken!, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChange_WithoutTheSessionsCsrfToken_IsRefused_AndWithItGoesThrough()
    {
        await using var rig = new Rig(_fixture);
        await rig.SignInAsync();
        var product = new CreateProductCommand { Name = "Cookie product", Description = "Made through a browser session", Price = 5m, Currency = "USD", Stock = 1 };

        var refused = await rig.Client.PostAsJsonAsync("/api/v1/products", product);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains(BrowserSessionEndpoints.CsrfHeader, await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products") { Content = JsonContent.Create(product) };
        wrong.Headers.Add(BrowserSessionEndpoints.CsrfHeader, "not-the-token");
        Assert.Equal(HttpStatusCode.Forbidden, (await rig.Client.SendAsync(wrong)).StatusCode);

        using var right = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products") { Content = JsonContent.Create(product) };
        right.Headers.Add(BrowserSessionEndpoints.CsrfHeader, await rig.CsrfTokenAsync());
        Assert.Equal(HttpStatusCode.Created, (await rig.Client.SendAsync(right)).StatusCode);
    }

    [Fact]
    public async Task TheCallback_RefusesAStateItDidNotIssue_AnotherBrowsersSignIn_AndAReplay()
    {
        await using var rig = new Rig(_fixture);

        var unknown = await rig.Client.GetAsync($"{BrowserSessionEndpoints.CallbackPath}?code=the-code&state=made-up");
        Assert.Equal($"{BrowserSessionEndpoints.SignedOutPage}?reason=failed", unknown.Headers.Location!.ToString());

        var login = await rig.Client.GetAsync(BrowserSessionEndpoints.LoginPath);
        var state = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();
        using var otherBrowser = rig.NewBrowser();
        var stolen = await otherBrowser.GetAsync($"{BrowserSessionEndpoints.CallbackPath}?code=the-code&state={state}");
        Assert.Equal($"{BrowserSessionEndpoints.SignedOutPage}?reason=failed", stolen.Headers.Location!.ToString());
        Assert.False(stolen.Headers.TryGetValues("Set-Cookie", out var set) && set.Any(value => value.StartsWith(BrowserSessionCookies.Session + "=", StringComparison.Ordinal) && !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)));

        var first = await rig.SignInAsync();
        Assert.Equal("/", first.Headers.Location!.ToString());
        Assert.Single(rig.Provider.Requests, request => request["grant_type"] == "authorization_code");
    }

    [Fact]
    public async Task ReturnTo_OnlyEverNamesAPathOnThisSite()
    {
        Assert.Equal("/orders", BrowserSessionEndpoints.LocalPath("/orders"));
        Assert.Equal("/", BrowserSessionEndpoints.LocalPath("https://evil.example"));
        Assert.Equal("/", BrowserSessionEndpoints.LocalPath("//evil.example"));
        Assert.Equal("/", BrowserSessionEndpoints.LocalPath("/\\evil.example"));
        Assert.Equal("/", BrowserSessionEndpoints.LocalPath(null));

        await using var rig = new Rig(_fixture);
        var callback = await rig.SignInAsync("//evil.example/x");
        Assert.Equal("/", callback.Headers.Location!.ToString());
    }

    [Fact]
    public async Task ASessionLeftIdle_Ends_SaysWhy_AndRevokesItsRefreshToken()
    {
        await using var rig = new Rig(_fixture);
        await rig.SignInAsync();

        rig.Clock.Advance(TimeSpan.FromMinutes(31));
        var response = await rig.Client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("idle", response.Headers.GetValues(BrowserSessionEndpoints.EndedHeader).Single());
        Assert.Contains(rig.Provider.Requests, request => request.ContainsKey("token_type_hint") && request["token"] == "refresh-1");

        rig.Clock.Advance(TimeSpan.FromMinutes(-31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await rig.Client.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task AnAccessTokenNearItsEnd_IsRefreshed_AndARefusedRefreshEndsTheSession()
    {
        await using var rig = new Rig(_fixture);
        await rig.SignInAsync();

        rig.Clock.Advance(TimeSpan.FromMinutes(4.5));
        Assert.Equal(HttpStatusCode.OK, (await rig.Client.GetAsync("/api/v1/products")).StatusCode);
        Assert.Contains(rig.Provider.Requests, request => request["grant_type"] == "refresh_token" && request["refresh_token"] == "refresh-1");

        rig.Provider.RefuseRefresh = true;
        rig.Clock.Advance(TimeSpan.FromMinutes(4.5));
        var refused = await rig.Client.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("expired", refused.Headers.GetValues(BrowserSessionEndpoints.EndedHeader).Single());
    }

    [Fact]
    public async Task SignOut_NeedsTheCsrfToken_ThenEndsTheSessionHere_AndAtTheProvider()
    {
        await using var rig = new Rig(_fixture);
        await rig.SignInAsync();

        var refused = await rig.Client.PostAsync(BrowserSessionEndpoints.LogoutPath, new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var signedOut = await rig.Client.PostAsync(BrowserSessionEndpoints.LogoutPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["csrf"] = await rig.CsrfTokenAsync(),
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        var location = signedOut.Headers.Location!;
        Assert.StartsWith($"{ProviderOrigin}/logout?", location.ToString(), StringComparison.Ordinal);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("id-1", query["id_token_hint"]);
        Assert.Equal($"{WebOrigin}{BrowserSessionEndpoints.SignedOutPage}?reason=signed-out", query["post_logout_redirect_uri"]);
        Assert.Contains(rig.Provider.Requests, request => request.ContainsKey("token_type_hint") && request["token"] == "refresh-1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await rig.Client.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task ABearerToken_StillWins_OverACookie()
    {
        await using var rig = new Rig(_fixture);
        await rig.SignInAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, BrowserSessionEndpoints.SessionPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtIdentity.CreateToken(subject: "someone-else"));
        var session = await (await rig.Client.SendAsync(request)).Content.ReadFromJsonAsync<BrowserSessionResponse>();

        Assert.Equal("someone-else", session!.Subject);
        Assert.Null(session.CsrfToken);
    }

    [Fact]
    public async Task StepUp_PassesOnlyTheConfiguredLevel_AndScopesOnlyFromTheConfiguredSet()
    {
        await using var rig = new Rig(_fixture);

        var stepUp = await rig.Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?acr_values={Uri.EscapeDataString(TestJwtIdentity.StepUpAcrValues)}");
        Assert.Equal(TestJwtIdentity.StepUpAcrValues, QueryHelpers.ParseQuery(stepUp.Headers.Location!.Query)["acr_values"]);

        var other = await rig.Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?acr_values=urn:anything");
        Assert.False(QueryHelpers.ParseQuery(other.Headers.Location!.Query).ContainsKey("acr_values"));

        var narrower = await rig.Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?scope={Uri.EscapeDataString("openid products:read")}");
        Assert.Equal("openid products:read", QueryHelpers.ParseQuery(narrower.Headers.Location!.Query)["scope"]);

        Assert.Equal(HttpStatusCode.BadRequest, (await rig.Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?scope=openid%20admin")).StatusCode);
    }

    [Fact]
    public async Task WithNoSessionClientConfigured_TheRoutesSaySo()
    {
        using var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(BrowserSessionEndpoints.LoginPath)).StatusCode);
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly WebApplicationFactory<IApiMarker> _factory;

        public Rig(ApiTestFixture fixture)
        {
            Provider = new StubTokenEndpoint();
            _factory = fixture.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Identity:Session:ClientId"] = "starterapp-session",
                    ["Identity:Session:ClientSecret"] = ClientSecret,
                    ["Identity:Session:WebOrigin"] = WebOrigin,
                    ["Identity:Session:Scope"] = "openid " + TestJwtIdentity.DefaultScopes,
                    ["Identity:Session:IdleMinutes"] = "30",
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock);
                    services.AddHttpClient(IdentityProviderClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Provider);
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                        options.ConfigurationManager = new FixedConfiguration(Discovery()));
                });
            });
            Client = NewBrowser();
        }

        public ManualClock Clock { get; } = new(DateTimeOffset.UtcNow);

        public StubTokenEndpoint Provider { get; }

        public HttpClient Client { get; }

        public HttpClient NewBrowser() =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });

        public async Task<HttpResponseMessage> SignInAsync(string returnTo = "/")
        {
            var login = await Client.GetAsync($"{BrowserSessionEndpoints.LoginPath}?returnTo={Uri.EscapeDataString(returnTo)}");
            var state = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["state"].ToString();
            var callback = await Client.GetAsync($"{BrowserSessionEndpoints.CallbackPath}?code=the-code&state={Uri.EscapeDataString(state)}");
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            return callback;
        }

        public async Task<string> CsrfTokenAsync()
        {
            var session = await Client.GetFromJsonAsync<BrowserSessionResponse>(BrowserSessionEndpoints.SessionPath);
            return session!.CsrfToken!;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
        }

        private static OpenIdConnectConfiguration Discovery()
        {
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = TestJwtIdentity.Issuer,
                AuthorizationEndpoint = $"{ProviderOrigin}/authorize",
                TokenEndpoint = $"{ProviderOrigin}/token",
                EndSessionEndpoint = $"{ProviderOrigin}/logout",
            };
            configuration.AdditionalData["revocation_endpoint"] = $"{ProviderOrigin}/revoke";
            configuration.SigningKeys.Add(TestJwtIdentity.SigningKey);
            return configuration;
        }
    }

    private sealed class FixedConfiguration : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration;

        public FixedConfiguration(OpenIdConnectConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) => Task.FromResult(_configuration);

        public void RequestRefresh()
        {
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class StubTokenEndpoint : HttpMessageHandler
    {
        private int _issued;

        public List<Dictionary<string, string>> Requests { get; } = [];

        public List<string> Authorizations { get; } = [];

        public string? LastAccessToken { get; private set; }

        public bool RefuseRefresh { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            lock (Requests)
            {
                Requests.Add(form);
                Authorizations.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            }

            if (request.RequestUri!.AbsolutePath == "/revoke")
                return new HttpResponseMessage(HttpStatusCode.OK);

            if (form["grant_type"] == "refresh_token" && RefuseRefresh)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };

            var issued = Interlocked.Increment(ref _issued);
            LastAccessToken = TestJwtIdentity.CreateToken();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Dictionary<string, object>
                {
                    ["access_token"] = LastAccessToken,
                    ["refresh_token"] = issued == 1 ? "refresh-1" : $"refresh-{issued}",
                    ["id_token"] = "id-1",
                    ["token_type"] = "Bearer",
                    ["expires_in"] = 300,
                }),
            };
        }
    }
}
