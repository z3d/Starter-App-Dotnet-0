using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class TwoFactorEndpointFilter : IEndpointFilter
{
    public const string RequiredAuthenticationMethod = "mfa";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var requiredMethods = context.HttpContext.GetEndpoint()
            ?.Metadata
            .GetOrderedMetadata<TwoFactorRequiredMetadata>() ?? [];

        if (requiredMethods.Count == 0)
            return await next(context);

        var currentUser = context.HttpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is not { IsAuthenticated: true })
            return BearerChallenges.Unauthenticated(context);

        var missingAuthenticationMethod = requiredMethods
            .Select(metadata => metadata.AuthenticationMethod)
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(authenticationMethod => !currentUser.HasAuthenticationMethod(authenticationMethod));

        if (missingAuthenticationMethod == null)
            return await next(context);

        // acr_values is advisory routing for the IdP; the access check stays amr-based.
        var acrValues = context.HttpContext.RequestServices
            .GetRequiredService<IOptions<JwtIdentityOptions>>().Value.StepUpAcrValues;
        return BearerChallenges.Forbidden(context, "Forbidden", "Two-factor authentication is required for this operation.",
            BearerChallenges.InsufficientUserAuthentication(acrValues));
    }
}
