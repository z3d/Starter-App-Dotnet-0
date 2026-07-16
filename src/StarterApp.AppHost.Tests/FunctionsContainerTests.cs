namespace StarterApp.AppHost.Tests;

[Collection("Aspire E2E")]
[Trait("Category", "Aspire")]
public class FunctionsContainerTests
{
    private readonly AspireE2EFixture _fixture;

    public FunctionsContainerTests(AspireE2EFixture fixture)
    {
        _fixture = fixture;
    }

    // The CI docker-build job proves the Functions image BUILDS; nothing else ever proves it
    // BOOTS. This fact pays the in-container image build + boot cost (via the fixture's lazy
    // gate) and pins that the deployable subscriber container actually comes up and serves.
    [Fact]
    public async Task FunctionsContainer_ShouldBuildBootAndServe()
    {
        await _fixture.EnsureFunctionsReadyAsync();
    }
}
