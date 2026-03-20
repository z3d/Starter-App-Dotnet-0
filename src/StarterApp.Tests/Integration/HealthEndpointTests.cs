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
}
