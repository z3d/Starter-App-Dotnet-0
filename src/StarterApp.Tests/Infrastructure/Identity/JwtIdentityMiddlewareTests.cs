using System.Security.Claims;

namespace StarterApp.Tests.Infrastructure.Identity;

// The bearer handler hands the middleware a ClaimsPrincipal whose multi-value claims differ by
// IdP: Entra emits space-delimited "scp", Keycloak a space-delimited "scope" and a JSON-array
// "amr" that can arrive as one claim holding the serialized array. These tests feed each shape
// straight into the middleware, which the integration suite cannot do because its tokens are
// always flattened into repeated claims by the handler.
public class JwtIdentityMiddlewareTests
{
    [Fact]
    public async Task JsonArrayClaim_IsSplitIntoItsElements()
    {
        var user = await RunAsync(("sub", "user-1"), ("tid", "tenant-1"), ("amr", "[\"mfa\",\"pwd\"]"), ("scope", "orders:read orders:write"));

        Assert.True(user.IsAuthenticated);
        Assert.True(user.HasAuthenticationMethod("mfa"));
        Assert.True(user.HasAuthenticationMethod("pwd"));
        Assert.True(user.HasScope("orders:read"));
        Assert.True(user.HasScope("orders:write"));
    }

    [Fact]
    public async Task RepeatedClaims_AreAllCollected()
    {
        var user = await RunAsync(("sub", "user-1"), ("tid", "tenant-1"), ("amr", "mfa"), ("amr", "pwd"));

        Assert.True(user.HasAuthenticationMethod("mfa"));
        Assert.True(user.HasAuthenticationMethod("pwd"));
    }

    [Fact]
    public async Task EntraScpClaim_IsReadAsScopes()
    {
        var user = await RunAsync(("sub", "user-1"), ("tid", "tenant-1"), ("scp", "customers:read customers:write"));

        Assert.True(user.HasScope("customers:read"));
        Assert.True(user.HasScope("customers:write"));
    }

    [Theory]
    [InlineData("[[\"mfa\"]]")]
    [InlineData("[{\"method\":\"mfa\"}]")]
    [InlineData("[1,2]")]
    [InlineData("[\"mfa\"")]
    public async Task MalformedJsonArrayClaim_GrantsNothing(string amr)
    {
        // Anything that is not a flat array of strings falls back to space-splitting the raw
        // text, and none of those fragments can equal "mfa".
        var user = await RunAsync(("sub", "user-1"), ("tid", "tenant-1"), ("amr", amr));

        Assert.True(user.IsAuthenticated);
        Assert.False(user.HasAuthenticationMethod("mfa"));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("tid")]
    public async Task MissingSubjectOrTenant_LeavesTheUserAnonymous(string missingClaim)
    {
        var claims = new List<(string, string)> { ("sub", "user-1"), ("tid", "tenant-1"), ("amr", "mfa") };
        claims.RemoveAll(claim => claim.Item1 == missingClaim);

        var user = await RunAsync(claims.ToArray());

        Assert.False(user.IsAuthenticated);
        Assert.Equal(string.Empty, user.TenantId);
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_LeavesTheUserAnonymous()
    {
        var accessor = new CurrentUserAccessor();
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        await new JwtIdentityMiddleware(_ => Task.CompletedTask).InvokeAsync(context, accessor);

        Assert.False(accessor.IsAuthenticated);
    }

    private static async Task<ICurrentUser> RunAsync(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var accessor = new CurrentUserAccessor();

        await new JwtIdentityMiddleware(_ => Task.CompletedTask).InvokeAsync(context, accessor);

        return accessor;
    }
}
