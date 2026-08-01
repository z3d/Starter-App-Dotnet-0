using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure.Identity;

public sealed class JwtIdentityOptions
{
    public const string SectionName = "Identity";

    // OIDC authority (e.g. https://idp.example.com/realms/starterapp). The bearer handler fetches
    // discovery + JWKS from here and caches them in memory; required outside Development/Testing.
    public string? Authority { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    // False only for the local dev Keycloak over http; options validation rejects it elsewhere.
    public bool RequireHttpsMetadata { get; set; } = true;

    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;
}
