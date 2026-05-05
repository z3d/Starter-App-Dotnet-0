using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Identity;

public class GatewayIdentityHeadersTests
{
    [Fact]
    public void Read_WithValidHeaders_ShouldReturnCurrentUserAndStableHeaderHash()
    {
        var context = CreateContext();

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.Equal("user-123", result.Envelope.User.Subject);
        Assert.Equal("tenant-123", result.Envelope.User.TenantId);
        Assert.True(result.Envelope.User.HasScope("orders:read"));
        Assert.True(result.Envelope.User.HasScope("products:write"));
        Assert.False(string.IsNullOrWhiteSpace(result.Envelope.HeaderHash));
    }

    [Fact]
    public void Read_WithScopesInDifferentOrder_ShouldProduceSameHeaderHash()
    {
        var first = CreateContext();
        var second = CreateContext();
        second.Request.Headers[GatewayIdentityHeaders.Scopes] = "orders:read products:write";

        var firstResult = GatewayIdentityHeaders.Read(first.Request.Headers);
        var secondResult = GatewayIdentityHeaders.Read(second.Request.Headers);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.NotNull(firstResult.Envelope);
        Assert.NotNull(secondResult.Envelope);
        Assert.Equal(firstResult.Envelope.HeaderHash, secondResult.Envelope.HeaderHash);
    }

    [Fact]
    public void Read_WithMissingRequiredHeader_ShouldFail()
    {
        var context = CreateContext();
        context.Request.Headers.Remove(GatewayIdentityHeaders.Subject);

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(GatewayIdentityHeaders.Subject, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithUnsupportedAuthenticatedHeader_ShouldFailClosed()
    {
        var context = CreateContext();
        context.Request.Headers["X-Authenticated-Roles"] = "Admin";

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("Unsupported identity header", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithDuplicateIdentityHeader_ShouldFail()
    {
        var context = CreateContext();
        context.Request.Headers[GatewayIdentityHeaders.Subject] = new StringValues(["user-123", "user-456"]);

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("exactly one value", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Orders:Read")]
    [InlineData("orders/read")]
    [InlineData("orders:read!")]
    public void Read_WithInvalidScopeShape_ShouldFail(string scope)
    {
        var context = CreateContext();
        context.Request.Headers[GatewayIdentityHeaders.Scopes] = scope;

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(GatewayIdentityHeaders.Scopes, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithLowercasePrincipalType_ShouldFail()
    {
        var context = CreateContext();
        context.Request.Headers[GatewayIdentityHeaders.PrincipalType] = "user";

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(GatewayIdentityHeaders.PrincipalType, StringComparison.Ordinal));
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[GatewayIdentityHeaders.Subject] = "user-123";
        context.Request.Headers[GatewayIdentityHeaders.PrincipalType] = AuthenticatedPrincipalType.User.ToString();
        context.Request.Headers[GatewayIdentityHeaders.TenantId] = "tenant-123";
        context.Request.Headers[GatewayIdentityHeaders.Scopes] = "products:write orders:read";
        context.Request.Headers[CorrelationContext.HeaderName] = "case-123";
        return context;
    }
}
