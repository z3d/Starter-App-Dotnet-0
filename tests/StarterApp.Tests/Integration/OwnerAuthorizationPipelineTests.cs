namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class OwnerAuthorizationPipelineTests
{
    private readonly ApiTestFixture _fixture;

    public OwnerAuthorizationPipelineTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void OwnerAuthorizationBehavior_IsRegisteredInTheApiPipeline()
    {
        using var scope = _fixture.Services.CreateScope();

        var behaviors = scope.ServiceProvider.GetServices<IPipelineBehavior<UpdateProductCommand, ProductDto?>>();

        Assert.Contains(behaviors, b => b is OwnerAuthorizationBehavior<UpdateProductCommand, ProductDto?>);
    }
}
