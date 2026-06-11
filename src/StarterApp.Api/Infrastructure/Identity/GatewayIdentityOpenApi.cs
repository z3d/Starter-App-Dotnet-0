using Microsoft.OpenApi;

namespace StarterApp.Api.Infrastructure.Identity;

// Declares the projected gateway-identity headers as apiKey security schemes on the OpenAPI
// document so Scalar renders a one-time Auth panel for them (without declared schemes Scalar
// offers no global header entry and every Test Request 401s against the /api/v1 surface).
// Lives in the identity layer because it is the only place allowed to know the header names
// (convention-enforced: GatewayIdentityHeaders_MustOnlyBeReadByIdentityInfrastructure). The
// document is served in Development only (MapOpenApi is dev-gated), where UnsignedDevelopment
// trusts these headers; in production the gateway strips and re-projects them.
public static class GatewayIdentityOpenApi
{
    public static void ApplySecuritySchemes(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        var identityHeaders = new[]
        {
            ("GatewaySubject", GatewayIdentityHeaders.Subject),
            ("GatewayPrincipalType", GatewayIdentityHeaders.PrincipalType),
            ("GatewayTenantId", GatewayIdentityHeaders.TenantId),
            ("GatewayScopes", GatewayIdentityHeaders.Scopes),
            ("GatewayAmr", GatewayIdentityHeaders.AuthenticationMethods)
        };

        var requirement = new OpenApiSecurityRequirement();
        foreach (var (schemeId, headerName) in identityHeaders)
        {
            document.Components.SecuritySchemes[schemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = headerName,
                Description = $"Projected gateway identity header ({headerName}); UnsignedDevelopment trusts it locally."
            };
            requirement[new OpenApiSecuritySchemeReference(schemeId, document)] = [];
        }

        document.Security ??= [];
        document.Security.Add(requirement);
    }
}
