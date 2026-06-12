using Microsoft.Extensions.Primitives;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Infrastructure.Identity;

public class GatewayIdentityHeadersTests
{
    [Fact]
    public void Read_WithValidHeaders_ShouldReturnCurrentUser()
    {
        var context = CreateContext();

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        Assert.Equal("user-123", result.Envelope.User.Subject);
        Assert.Equal("tenant-123", result.Envelope.User.TenantId);
        Assert.True(result.Envelope.User.HasScope("orders:read"));
        Assert.True(result.Envelope.User.HasScope("products:write"));
        Assert.True(result.Envelope.User.HasAuthenticationMethod("mfa"));
        Assert.True(result.Envelope.User.HasAuthenticationMethod("pwd"));
    }

    [Fact]
    public void Read_WithScopesInDifferentOrder_ShouldParseTheSameScopeSet()
    {
        // Header ordering is not semantic: the parsed sets are what the validator compares
        // (sorted on both sides), so order-insensitivity is a set property, not a hash property.
        var first = CreateContext();
        var second = CreateContext();
        second.Request.Headers[GatewayIdentityHeaders.Scopes] = "orders:read products:write";

        var firstResult = GatewayIdentityHeaders.Read(first.Request.Headers);
        var secondResult = GatewayIdentityHeaders.Read(second.Request.Headers);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.NotNull(firstResult.Envelope);
        Assert.NotNull(secondResult.Envelope);
        Assert.Equal(
            firstResult.Envelope.User.Scopes.OrderBy(s => s, StringComparer.Ordinal),
            secondResult.Envelope.User.Scopes.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void Read_WithAuthenticationMethodsInDifferentOrder_ShouldParseTheSameMethodSet()
    {
        var first = CreateContext();
        var second = CreateContext();
        second.Request.Headers[GatewayIdentityHeaders.AuthenticationMethods] = "pwd mfa";

        var firstResult = GatewayIdentityHeaders.Read(first.Request.Headers);
        var secondResult = GatewayIdentityHeaders.Read(second.Request.Headers);

        Assert.True(firstResult.Succeeded);
        Assert.True(secondResult.Succeeded);
        Assert.NotNull(firstResult.Envelope);
        Assert.NotNull(secondResult.Envelope);
        Assert.Equal(
            firstResult.Envelope.User.AuthenticationMethods.OrderBy(m => m, StringComparer.Ordinal),
            secondResult.Envelope.User.AuthenticationMethods.OrderBy(m => m, StringComparer.Ordinal));
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

    [Theory]
    [InlineData("Mfa")]
    [InlineData("mfa/otp")]
    [InlineData("mfa!")]
    public void Read_WithInvalidAuthenticationMethodShape_ShouldFail(string authenticationMethod)
    {
        var context = CreateContext();
        context.Request.Headers[GatewayIdentityHeaders.AuthenticationMethods] = authenticationMethod;

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(GatewayIdentityHeaders.AuthenticationMethods, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("trace:abc123")]
    [InlineData("svc/abc123")]
    [InlineData("abc 123")]
    [InlineData("abc+123")]
    public void Read_WithCorrelationIdOutsideContractCharset_ShouldFail(string correlationId)
    {
        // The gateway signs the assertion over the raw correlation id, so the verifier must not silently
        // rewrite it (the old Sanitize() path). Out-of-charset ids are rejected deterministically instead.
        var context = CreateContext();
        context.Request.Headers[CorrelationContext.HeaderName] = correlationId;

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(CorrelationContext.HeaderName, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithOverlongCorrelationId_ShouldFail()
    {
        var context = CreateContext();
        context.Request.Headers[CorrelationContext.HeaderName] = new string('a', 129);

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(CorrelationContext.HeaderName, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_WithContractValidCorrelationId_ShouldStoreRawValueForSignerVerifierParity()
    {
        var context = CreateContext();
        context.Request.Headers[CorrelationContext.HeaderName] = "trace-abc.123_v2";

        var result = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Envelope);
        // Stored verbatim (not Sanitize-rewritten) so the verifier canonicalizes identically to the signer.
        Assert.Equal("trace-abc.123_v2", result.Envelope.User.CorrelationId);
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
        context.Request.Headers[GatewayIdentityHeaders.AuthenticationMethods] = "mfa pwd";
        context.Request.Headers[CorrelationContext.HeaderName] = "case-123";
        return context;
    }
}
