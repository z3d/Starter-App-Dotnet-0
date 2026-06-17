using Microsoft.Extensions.Options;
using StarterApp.Gateway.Proxy;
using StarterApp.ServiceDefaults.Payloads;
using Yarp.ReverseProxy.Transforms;

namespace StarterApp.Tests.Infrastructure.Identity;

// Parity pin between the gateway emulator (the assertion PRODUCER) and the API (the VERIFIER):
// two sides of one contract are exercised together so they can never fork silently.
public class GatewaySignerParityTests
{
    private const string SigningKey = "starterapp-test-gateway-signing-key-32-bytes-minimum";
    private const string KeyId = "parity-test-key";

    private static readonly string[] RequiredEndpointScopes =
    [
        "customers:read",
        "customers:write",
        "orders:read",
        "orders:write",
        "products:read",
        "products:write"
    ];

    [Fact]
    public void GatewaySignedAssertion_PassesApiValidation()
    {
        var projection = new GatewayIdentityProjection(
            "parity-user",
            "User",
            "parity-tenant",
            ["customers:read", "customers:write"],
            ["mfa", "pwd"],
            "parity-corr-1");

        var token = CreateSigner().CreateAssertion(projection, "POST", "/api/v1/orders");
        var context = CreateApiSideContext(projection, token, "POST", "/api/v1/orders");

        var read = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(read.Succeeded, string.Join("; ", read.Errors));
        Assert.NotNull(read.Envelope);

        var result = CreateValidator().Validate(context, read.Envelope);

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task DefaultDevIdentity_RoundTripsThroughTransform_AndCoversEveryEndpointScope()
    {
        var options = CreateOptions();
        var transformContext = CreateTransformContext("POST", "/api/v1/orders");

        await ApplyTransformAsync(transformContext, options);

        var context = CopyProxyRequestToApiSideContext(transformContext, "POST", "/api/v1/orders");
        var read = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(read.Succeeded, string.Join("; ", read.Errors));
        Assert.NotNull(read.Envelope);

        var result = CreateValidator().Validate(context, read.Envelope);

        Assert.True(result.Succeeded, result.Error);
        foreach (var scope in RequiredEndpointScopes)
            Assert.True(read.Envelope.User.HasScope(scope), $"Default dev identity must cover endpoint scope '{scope}'.");
        Assert.True(read.Envelope.User.HasAuthenticationMethod("mfa"), "Default dev identity must satisfy SecuredBy2Fa.");
    }

    [Fact]
    public void TamperedAssertionPayload_FailsApiValidation()
    {
        var projection = new GatewayIdentityProjection(
            "parity-user",
            "User",
            "parity-tenant",
            ["customers:read"],
            ["mfa"],
            "parity-corr-2");

        var token = CreateSigner().CreateAssertion(projection, "GET", "/api/v1/orders/x");
        var parts = token.Split('.');
        var tamperedPayload = parts[1][..^1] + (parts[1][^1] == 'A' ? 'B' : 'A');
        var tamperedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var context = CreateApiSideContext(projection, tamperedToken, "GET", "/api/v1/orders/x");
        var read = GatewayIdentityHeaders.Read(context.Request.Headers);

        Assert.True(read.Succeeded, string.Join("; ", read.Errors));
        Assert.NotNull(read.Envelope);

        var result = CreateValidator().Validate(context, read.Envelope);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Transform_StripsCallerIdentity_AndSignsTheRawCorrelationId()
    {
        var transformContext = CreateTransformContext("POST", "/api/v1/orders");
        var incoming = transformContext.HttpContext.Request.Headers;
        incoming["X-Authenticated-Subject"] = "caller-stated-subject";
        incoming["X-Authenticated-Garbage"] = "junk";
        incoming[GatewayIdentityHeaders.Assertion] = "v1.forged.forged";
        incoming[CorrelationContext.HeaderName] = "client-corr-7";
        CopyIncomingHeadersToProxyRequest(transformContext);

        await ApplyTransformAsync(transformContext, CreateOptions());

        var proxyHeaders = transformContext.ProxyRequest.Headers;
        Assert.False(proxyHeaders.Contains("X-Authenticated-Garbage"), "Inbound junk identity headers must be stripped.");
        Assert.Equal("caller-stated-subject", Assert.Single(proxyHeaders.GetValues(GatewayIdentityHeaders.Subject)));
        Assert.Equal("client-corr-7", Assert.Single(proxyHeaders.GetValues(CorrelationContext.HeaderName)));

        var token = Assert.Single(proxyHeaders.GetValues(GatewayIdentityHeaders.Assertion));
        Assert.NotEqual("v1.forged.forged", token);
        Assert.True(GatewayAssertionToken.TryRead(token, out _, out var payloadSegment, out _));
        var payload = GatewayAssertionToken.ReadPayload(payloadSegment);
        Assert.NotNull(payload);
        Assert.Equal("client-corr-7", payload.CorrelationId);
        Assert.Equal("caller-stated-subject", payload.Subject);
    }

    private static GatewaySignerOptions CreateOptions()
    {
        return new GatewaySignerOptions
        {
            Issuer = "apim",
            Audience = "starterapp-api",
            SigningKey = SigningKey,
            KeyId = KeyId,
        };
    }

    private static GatewayAssertionSigner CreateSigner()
    {
        return new GatewayAssertionSigner(Options.Create(CreateOptions()), TimeProvider.System);
    }

    private static GatewayAssertionValidator CreateValidator()
    {
        return new GatewayAssertionValidator(
            Options.Create(new GatewayIdentityOptions
            {
                Mode = GatewayIdentityMode.Required,
                Issuer = "apim",
                Audience = "starterapp-api",
                SigningKey = SigningKey,
                KeyId = KeyId,
            }),
            TimeProvider.System);
    }

    private static RequestTransformContext CreateTransformContext(string method, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;

        return new RequestTransformContext
        {
            HttpContext = httpContext,
            ProxyRequest = new HttpRequestMessage(HttpMethod.Parse(method), new Uri($"http://api{path}")),
            Path = httpContext.Request.Path,
            HeadersCopied = true,
        };
    }

    private static async Task ApplyTransformAsync(RequestTransformContext transformContext, GatewaySignerOptions options)
    {
        var wrappedOptions = Options.Create(options);
        var provider = new GatewayAssertionTransformProvider(
            new GatewayIdentityProjectionResolver(wrappedOptions),
            new GatewayAssertionSigner(wrappedOptions, TimeProvider.System));

        var builderContext = new Yarp.ReverseProxy.Transforms.Builder.TransformBuilderContext
        {
            Services = new ServiceCollection().BuildServiceProvider(),
        };
        provider.Apply(builderContext);

        foreach (var transform in builderContext.RequestTransforms)
            await transform.ApplyAsync(transformContext);
    }

    // Simulates YARP's default header-copy transform, which runs before custom transforms.
    private static void CopyIncomingHeadersToProxyRequest(RequestTransformContext transformContext)
    {
        foreach (var header in transformContext.HttpContext.Request.Headers)
            transformContext.ProxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }

    private static DefaultHttpContext CreateApiSideContext(GatewayIdentityProjection projection, string token, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers[GatewayIdentityHeaders.Subject] = projection.Subject;
        context.Request.Headers[GatewayIdentityHeaders.PrincipalType] = projection.PrincipalType;
        context.Request.Headers[GatewayIdentityHeaders.TenantId] = projection.TenantId;
        context.Request.Headers[GatewayIdentityHeaders.Scopes] = string.Join(' ', projection.Scopes);
        context.Request.Headers[GatewayIdentityHeaders.AuthenticationMethods] = string.Join(' ', projection.AuthenticationMethods);
        context.Request.Headers[CorrelationContext.HeaderName] = projection.CorrelationId;
        context.Request.Headers[GatewayIdentityHeaders.Assertion] = token;
        return context;
    }

    // Rebuilds the request the API receives from the outgoing proxy request the transform produced.
    private static DefaultHttpContext CopyProxyRequestToApiSideContext(RequestTransformContext transformContext, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        foreach (var header in transformContext.ProxyRequest.Headers)
            context.Request.Headers[header.Key] = header.Value.ToArray();
        return context;
    }
}
