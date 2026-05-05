namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class GatewayIdentityIntegrationTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public GatewayIdentityIntegrationTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task HealthEndpoint_WithoutGatewayIdentity_ShouldRemainPublic()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/alive");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutGatewayIdentity_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithSignedGatewayIdentity_ShouldSucceed()
    {
        var response = await _fixture.Client.GetAsync("/api/v1/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithIdentityHeadersButMissingAssertion_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddUnsignedHeaders(request);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredAssertion_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        TestGatewayIdentity.AddSignedHeaders(request, issuedAt: issuedAt, expiresAt: issuedAt.AddSeconds(60));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, audience: "wrong-api");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongPath_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, path: "/api/v1/customers");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongMethod_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, method: HttpMethod.Post.Method);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedIdentityHeader_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request);
        request.Headers.Remove(GatewayIdentityHeaders.Subject);
        request.Headers.Add(GatewayIdentityHeaders.Subject, "attacker");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongSigningKey_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, signingKey: "wrong-signing-key-with-enough-length-to-sign");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithUnknownKeyId_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, keyId: "unknown-key");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMissingKeyIdWhenConfigured_ShouldReturnUnauthorized()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/products");
        TestGatewayIdentity.AddSignedHeaders(request, keyId: null);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
