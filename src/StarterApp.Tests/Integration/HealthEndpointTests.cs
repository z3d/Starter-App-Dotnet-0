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
    public async Task HealthEndpoints_ShouldReturnSuccess(string path)
    {
        var response = await _fixture.Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_AnswersFromTheProcessAloneWithoutEvaluatingDependencies()
    {
        var response = await _fixture.Client.GetAsync("/liveness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("alive", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.TryGetProperty("timestampUtc", out _));
    }

    [Fact]
    public async Task Healthiness_DeepProbesTheDurableResources()
    {
        var response = await _fixture.Client.GetAsync("/healthiness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

        var checks = body.RootElement.GetProperty("checks").EnumerateArray()
            .ToDictionary(check => check.GetProperty("name").GetString()!, check => check.GetProperty("status").GetString());

        // The database is always in the durable set; Redis (distributed-cache), Service Bus and the
        // payload archive join only when configured (this fixture runs without them, like standalone dev).
        Assert.Equal("Healthy", checks["database"]);
    }
}
