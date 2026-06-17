using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace StarterApp.Gateway.Proxy;

internal sealed class GatewayAssertionTransformProvider : ITransformProvider
{
    private readonly GatewayIdentityProjectionResolver _resolver;
    private readonly GatewayAssertionSigner _signer;

    public GatewayAssertionTransformProvider(GatewayIdentityProjectionResolver resolver, GatewayAssertionSigner signer)
    {
        _resolver = resolver;
        _signer = signer;
    }

    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        context.RequestTransforms.Add(new GatewayAssertionRequestTransform(_resolver, _signer));
    }
}

// Emulates a trusted APIM gateway policy on the outgoing proxy request: strip whatever identity the
// caller sent, project a normalized identity, and sign the assertion the API verifies. Runs as a
// request transform (after YARP's default header copy) so stripped inbound headers cannot be
// re-copied over the projection.
internal sealed class GatewayAssertionRequestTransform : RequestTransform
{
    private readonly GatewayIdentityProjectionResolver _resolver;
    private readonly GatewayAssertionSigner _signer;

    public GatewayAssertionRequestTransform(GatewayIdentityProjectionResolver resolver, GatewayAssertionSigner signer)
    {
        _resolver = resolver;
        _signer = signer;
    }

    public override ValueTask ApplyAsync(RequestTransformContext context)
    {
        StripCallerIdentityHeaders(context);

        var incomingHeaders = context.HttpContext.Request.Headers;
        var correlationId = ResolveCorrelationId(incomingHeaders);
        var projection = _resolver.Resolve(incomingHeaders, correlationId);

        RemoveHeader(context, CorrelationContext.HeaderName);
        AddHeader(context, CorrelationContext.HeaderName, correlationId);
        AddHeader(context, GatewayIdentityHeaderNames.Subject, projection.Subject);
        AddHeader(context, GatewayIdentityHeaderNames.PrincipalType, projection.PrincipalType);
        AddHeader(context, GatewayIdentityHeaderNames.TenantId, projection.TenantId);
        AddHeader(context, GatewayIdentityHeaderNames.Scopes, string.Join(' ', projection.Scopes));

        if (projection.AuthenticationMethods.Count > 0)
            AddHeader(context, GatewayIdentityHeaderNames.AuthenticationMethods, string.Join(' ', projection.AuthenticationMethods));

        // Sign over the outgoing path — identical to the inbound path with the catch-all route,
        // and the value the API's validator compares the pth claim against.
        var method = context.HttpContext.Request.Method;
        var path = context.Path.Value ?? string.Empty;
        AddHeader(context, GatewayIdentityHeaderNames.Assertion, _signer.CreateAssertion(projection, method, path));

        return default;
    }

    // The gateway contract: inbound caller-supplied identity headers never pass through. The prefix
    // strip also removes unsupported X-Authenticated-* names the API would 401 on; the caller's
    // stated identity is re-read from the ORIGINAL request by the resolver.
    private static void StripCallerIdentityHeaders(RequestTransformContext context)
    {
        var identityHeaderNames = context.ProxyRequest.Headers
            .Select(header => header.Key)
            .Where(name => name.StartsWith("X-Authenticated-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var name in identityHeaderNames)
            RemoveHeader(context, name);

        RemoveHeader(context, GatewayIdentityHeaderNames.Assertion);
    }

    // A present correlation id passes through RAW: the assertion signs the exact value, and the
    // API verifies the signed value against the projected header — rewriting here would reject
    // valid traffic. Only generate when the caller sent none (charset-safe by construction).
    private static string ResolveCorrelationId(IHeaderDictionary incomingHeaders)
    {
        if (incomingHeaders.TryGetValue(CorrelationContext.HeaderName, out var values) && values.Count >= 1 && !string.IsNullOrWhiteSpace(values[0]))
            return values[0]!;

        return $"gw-{Guid.CreateVersion7():N}";
    }
}
