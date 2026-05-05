using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class GatewayIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayIdentityOptions _options;
    private readonly IGatewayAssertionValidator _assertionValidator;
    private readonly ILogger<GatewayIdentityMiddleware> _logger;

    public GatewayIdentityMiddleware(
        RequestDelegate next,
        IOptions<GatewayIdentityOptions> options,
        IGatewayAssertionValidator assertionValidator,
        ILogger<GatewayIdentityMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _assertionValidator = assertionValidator;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUserAccessor currentUserAccessor)
    {
        if (!RequiresGatewayIdentity(context))
        {
            await _next(context);
            return;
        }

        var identity = GatewayIdentityHeaders.Read(context.Request.Headers);
        if (!identity.Succeeded || identity.Envelope == null)
        {
            _logger.LogWarning("Rejected request with malformed gateway identity headers: {Reason}", string.Join("; ", identity.Errors));
            await WriteUnauthorizedAsync(context);
            return;
        }

        if (_options.Mode == GatewayIdentityMode.Required)
        {
            var assertion = _assertionValidator.Validate(context, identity.Envelope);
            if (!assertion.Succeeded)
            {
                _logger.LogWarning("Rejected request with invalid gateway assertion: {Reason}", assertion.Error);
                await WriteUnauthorizedAsync(context);
                return;
            }
        }

        currentUserAccessor.Set(identity.Envelope.User);
        await _next(context);
    }

    private static bool RequiresGatewayIdentity(HttpContext context)
    {
        return context.GetEndpoint()?.Metadata.GetMetadata<GatewayIdentityRequiredMetadata>() != null;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Unauthorized",
            Detail = "A valid gateway identity is required."
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
