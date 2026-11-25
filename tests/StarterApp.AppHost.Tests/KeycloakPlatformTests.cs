using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace StarterApp.AppHost.Tests;

public class KeycloakPlatformTests
{
    [Fact]
    public async Task TheLocalKeycloak_AsksForTheEnginesOwnPlatform()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.StarterApp_AppHost>();
        var keycloak = appHost.Resources.Single(resource => resource.Name == "keycloak");

        var context = new ContainerRuntimeArgsCallbackContext([], CancellationToken.None);
        foreach (var annotation in keycloak.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
            await annotation.Callback(context);

        var platform = await ContainerEngine.PlatformAsync(CancellationToken.None);
        Assert.Equal(platform is null ? [] : ["--platform", platform], context.Args.Cast<string>());
    }
}
