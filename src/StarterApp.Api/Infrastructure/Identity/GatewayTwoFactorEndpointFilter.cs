namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class GatewayTwoFactorEndpointFilter : IEndpointFilter
{
    public const string RequiredAuthenticationMethod = "mfa";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requiredMethods = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetOrderedMetadata<GatewayTwoFactorRequiredMetadata>() ?? [];

        if (requiredMethods.Count == 0)
            return await next(context);

        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return WriteProblem(StatusCodes.Status401Unauthorized, "Unauthorized", "A valid gateway identity is required.");

        var missingAuthenticationMethod = requiredMethods
            .Select(metadata => metadata.AuthenticationMethod)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(authenticationMethod => !currentUser.HasAuthenticationMethod(authenticationMethod));

        return missingAuthenticationMethod == null
            ? await next(context)
            : WriteProblem(StatusCodes.Status403Forbidden, "Forbidden", "Two-factor authentication is required for this operation.");
    }

    private static IResult WriteProblem(int statusCode, string title, string detail)
    {
        return Results.Problem(statusCode: statusCode, title: title, detail: detail);
    }
}
