namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class HealthEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public HealthEndpointTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/health/live")]
    [InlineData("/alive")]
    [InlineData("/liveness")]
    [InlineData("/healthiness")]
    public async Task HealthEndpoints_ShouldReturnSuccess(string path)
    {
        var response = await _fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Healthiness_ShouldReportDurableChecks()
    {
        var response = await _fixture.Client.GetAsync("/healthiness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        // The durable set always includes the database and the distributed cache (in-memory fallback
        // in the Testing environment); Service Bus / payload archive register only when configured.
        Assert.Contains("database", body, StringComparison.Ordinal);
        Assert.Contains("distributed-cache", body, StringComparison.Ordinal);
    }
}
