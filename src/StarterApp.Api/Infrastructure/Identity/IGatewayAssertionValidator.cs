namespace StarterApp.Api.Infrastructure.Identity;

internal interface IGatewayAssertionValidator
{
    GatewayAssertionValidationResult Validate(HttpContext context, GatewayIdentityEnvelope envelope);
}
