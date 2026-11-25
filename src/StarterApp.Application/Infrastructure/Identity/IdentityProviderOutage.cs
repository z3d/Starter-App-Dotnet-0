using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace StarterApp.Application.Infrastructure.Identity;

// A token cannot be checked while the provider's discovery document or signing keys cannot be fetched. That is an outage, not a bad token, so the caller gets a 503.
internal static class IdentityProviderOutage
{
    private static readonly object Marker = new();

    // The token handler reports a failed fetch as "no signing keys", so the configuration manager is asked again; it answers from its cache when the provider is up.
    public static async Task OnAuthenticationFailed(AuthenticationFailedContext context)
    {
        if (context.Options.ConfigurationManager is not { } configurationManager)
            return;

        try
        {
            await configurationManager.GetConfigurationAsync(context.HttpContext.RequestAborted);
        }
        catch (InvalidOperationException ex)
        {
            context.HttpContext.Items[Marker] = true;
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(IdentityProviderOutage))
                .LogError(ex, "The identity provider could not be reached to validate a token");
        }
    }

    public static async Task OnChallenge(JwtBearerChallengeContext context)
    {
        if (!context.HttpContext.Items.ContainsKey(Marker))
            return;

        context.HandleResponse();
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.Headers.RetryAfter = "30";
        await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service Unavailable",
                Detail = "The identity provider could not be reached, so the token could not be checked. Try again shortly."
            }
        });
    }
}
