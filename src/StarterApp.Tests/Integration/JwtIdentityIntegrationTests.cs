using System.Net.Http.Headers;

namespace StarterApp.Tests.Integration;

// The negative-path contract of the OIDC/JWT identity layer, exercised through the real
// JwtBearer handler: token absence, expiry, audience/issuer mismatch, and signature failure all
// end in 401 before any handler runs; scope and amr gaps end in 403 at the endpoint filters.
[Collection("Integration Tests")]
public class JwtIdentityIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public JwtIdentityIntegrationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task MissingToken_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var response = await client.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var expired = TestJwtIdentity.CreateToken(
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5));

        var response = await SendWithTokenAsync(expired);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Returns401()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(audience: "some-other-api"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_Returns401()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(issuer: "https://evil.example.test/realms/starterapp"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedByUntrustedKey_Returns401()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(signingKey: TestJwtIdentity.UntrustedKey));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingTenantClaim_Returns401()
    {
        // A validly signed token without tid must not authenticate: owner scoping, cache keys,
        // and rate-limit partitions key on subject + tenant, so an IdP missing its tenant
        // mapper has to fail loudly instead of stamping rows with an empty tenant.
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(tenantId: null));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSubjectClaim_Returns401()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(subject: ""));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_token", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingScope_Returns403()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken(scopes: "customers:read"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // RFC 6750 step-up challenge: advertises the endpoint's required scope set so a client
        // can re-authorize at the IdP and retry without parsing the problem detail.
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("insufficient_scope", challenge, StringComparison.Ordinal);
        Assert.Contains("scope=\"products:read\"", challenge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteWithoutMfaAmr_Returns403()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/products");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtIdentity.CreateToken(authenticationMethods: "pwd"));
        request.Content = JsonContent.Create(new CreateProductCommand
        {
            Name = "MFA Gap Product",
            Description = "Should never be created",
            Price = 10m,
            Currency = "USD",
            Stock = 1
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // RFC 9470 step-up challenge, carrying the configured acr_values so the client's next
        // authorization request can demand the MFA-satisfying ACR from the IdP.
        var challenge = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("insufficient_user_authentication", challenge, StringComparison.Ordinal);
        Assert.Contains($"acr_values=\"{TestJwtIdentity.StepUpAcrValues}\"", challenge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidToken_Succeeds()
    {
        var response = await SendWithTokenAsync(TestJwtIdentity.CreateToken());
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task HealthProbes_StayAnonymous()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(string token)
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
