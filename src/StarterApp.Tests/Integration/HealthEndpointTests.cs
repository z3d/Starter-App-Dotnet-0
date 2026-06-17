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
        // The database is always in the durable set; Redis (distributed-cache), Service Bus and the
        // payload archive register only when configured (none are in the Testing environment).
        Assert.Contains("database", body, StringComparison.Ordinal);
    }
}
