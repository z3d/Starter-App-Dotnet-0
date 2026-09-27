using Microsoft.AspNetCore.Mvc.Testing;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class RateLimitingPipelineTests
{
    private readonly ApiTestFixture _fixture;

    public RateLimitingPipelineTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnauthenticatedCallers_AreThrottledBeforeAuthorizationRejectsThem()
    {
        await using var factory = _fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PermitLimit", "2");
            builder.UseSetting("RateLimiting:QueueLimit", "0");
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            statuses.Add((await client.GetAsync("/api/v1/products")).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }
}
