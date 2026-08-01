using Microsoft.OpenApi;

namespace StarterApp.Api.Infrastructure.Identity;

// Declares the JWT bearer security scheme on the OpenAPI document so Scalar renders an Auth
// panel (without a declared scheme Scalar offers no token entry and every Test Request 401s
// against the /api/v1 surface). The document is served in Development only (MapOpenApi is
// dev-gated); acquire a token from the dev Keycloak realm.
public static class JwtIdentityOpenApi
{
    public static void ApplySecuritySchemes(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC access token; locally, mint one from the dev Keycloak realm (client starterapp-dev)."
        };

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        };

        document.Security ??= [];
        document.Security.Add(requirement);
    }
}
