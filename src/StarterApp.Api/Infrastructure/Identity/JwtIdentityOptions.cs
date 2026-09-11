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

    // Advisory acr_values advertised in the RFC 9470 step-up challenge when a write lacks the
    // mfa amr — the ACR the deployer's IdP uses to mean "MFA performed" (Keycloak LoA name,
    // Entra auth-context id). Optional: when unset the challenge carries the error code alone.
    public string? StepUpAcrValues { get; set; }

    // RSA and ECDSA only, which is what an OIDC JWKS document publishes. HMAC algorithms would
    // let anyone holding the shared secret mint tokens, and there is no such secret here.
    public static readonly IReadOnlyList<string> AllowedSigningAlgorithms =
    [
        "RS256", "RS384", "RS512",
        "PS256", "PS384", "PS512",
        "ES256", "ES384", "ES512",
    ];
}
