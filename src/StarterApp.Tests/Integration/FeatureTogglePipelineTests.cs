namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class FeatureTogglePipelineTests
{
    private readonly ApiTestFixture _fixture;

    public FeatureTogglePipelineTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void FeatureToggleBehavior_IsRegisteredOutermostInTheApiPipeline()
    {
        using var scope = _fixture.Services.CreateScope();

        var behaviors = scope.ServiceProvider
            .GetServices<IPipelineBehavior<UpdateProductCommand, ProductDto?>>()
            .ToList();

        // First registered runs outermost: a disabled feature must refuse dispatch
        // before the caching behavior could serve it from cache.
        Assert.IsType<FeatureToggleBehavior<UpdateProductCommand, ProductDto?>>(behaviors[0]);
    }
}
