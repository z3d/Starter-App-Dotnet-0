using System.Net.Sockets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace StarterApp.Tests.Integration;

// The bearer handler fetches the provider's discovery document and keys through a real ConfigurationManager here,
// so a provider that cannot be reached is told apart from a token that is wrong.
[Collection("Integration Tests")]
public class IdentityProviderOutageTests
{
    private readonly ApiTestFixture _fixture;

    public IdentityProviderOutageTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProviderUnreachable_Returns503_NamingTheProvider()
    {
        var retriever = new HttpDocumentRetriever { RequireHttps = false };
        await using var factory = WithConfigurationFrom($"http://127.0.0.1:{ClosedPort()}/realms/x/.well-known/openid-configuration", retriever);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestJwtIdentity.SetBearer(request);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("identity provider could not be reached", problem!.Detail, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("WWW-Authenticate"));

        var alive = await client.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [Fact]
    public async Task ProviderReachable_BadToken_StillReturns401()
    {
        await using var factory = WithConfigurationFrom(StubProvider.DiscoveryAddress, new StubProvider());
        using var client = factory.CreateClient();

        using var good = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestJwtIdentity.SetBearer(good);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(good)).StatusCode);

        using var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        bad.Headers.Authorization = new("Bearer", TestJwtIdentity.CreateToken(signingKey: TestJwtIdentity.UntrustedKey));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(bad)).StatusCode);
    }

    private WebApplicationFactory<IApiMarker> WithConfigurationFrom(string address, IDocumentRetriever retriever) =>
        _fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.IssuerSigningKey = null;
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    address, new OpenIdConnectConfigurationRetriever(), retriever);
            })));

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubProvider : IDocumentRetriever
    {
        public const string DiscoveryAddress = "https://stub-idp.example.test/.well-known/openid-configuration";
        private const string KeysAddress = "https://stub-idp.example.test/keys";

        public Task<string> GetDocumentAsync(string address, CancellationToken cancel)
        {
            if (address == DiscoveryAddress)
                return Task.FromResult(JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["issuer"] = TestJwtIdentity.Issuer,
                    ["jwks_uri"] = KeysAddress
                }));

            var key = TestJwtIdentity.SigningKey;
            var parameters = key.Rsa.ExportParameters(false);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                keys = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["kty"] = "RSA",
                        ["use"] = "sig",
                        ["alg"] = "RS256",
                        ["kid"] = key.KeyId!,
                        ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                        ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
                    }
                }
            }));
        }
    }
}
