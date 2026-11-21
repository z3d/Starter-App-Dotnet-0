using Microsoft.AspNetCore.Routing.Patterns;

namespace StarterApp.Tests.Integration;

// Every /api/v1 route, through the real host. The convention tests check the metadata; this checks what a caller gets.
[Collection("Integration Tests")]
public class RouteRefusalTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public RouteRefusalTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EveryApiRoute_RefusesNoTokenAndAWrongScope_AndAdmitsTheRightOne()
    {
        using var client = _fixture.CreateUnauthenticatedClient();
        var routes = _fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null)
            .ToList();
        Assert.NotEmpty(routes);

        foreach (var endpoint in routes)
        {
            var path = Fill(endpoint.RoutePattern);
            var scope = endpoint.Metadata.GetMetadata<ScopeRequiredMetadata>()?.Scope;
            var mfa = endpoint.Metadata.GetMetadata<TwoFactorRequiredMetadata>() is not null;
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                await RouteRefusals.AssertAsync(client, new HttpMethod(method), path, scope, mfa);
        }
    }

    private static string Fill(RoutePattern pattern) =>
        "/" + string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(policy => policy.Content == "guid") => Guid.CreateVersion7().ToString(),
            RoutePatternParameterPart parameter when parameter.ParameterPolicies.Any(policy => policy.Content is "int" or "long") => "1",
            RoutePatternParameterPart => "Pending",
            _ => string.Empty
        }))));
}
