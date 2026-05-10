namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class GatewayScopeEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requiredScopes = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetOrderedMetadata<GatewayScopeRequiredMetadata>() ?? [];

        if (requiredScopes.Count == 0)
            return await next(context);

        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return WriteProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "A valid gateway identity is required.");

        var missingScope = requiredScopes
            .Select(metadata => metadata.Scope)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(scope => !currentUser.HasScope(scope));

        return missingScope == null
            ? await next(context)
            : WriteProblem(StatusCodes.Status403Forbidden, "Forbidden", $"Required scope '{missingScope}' is missing.");
    }

    private static IResult WriteProblem(int statusCode, string title, string detail)
    {
        return Results.Problem(statusCode: statusCode, title: title, detail: detail);
    }
}
