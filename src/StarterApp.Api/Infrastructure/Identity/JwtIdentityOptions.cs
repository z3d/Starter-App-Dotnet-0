using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure.Identity;

public sealed class JwtIdentityOptions
{
    public const string SectionName = "Identity";

    // Required outside Development and Testing.
    public string? Authority { get; set; }

    // Where discovery is fetched when that differs from the public issuer address; the issuer stays Authority.
    public string? MetadataAddress { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    // False only for the local dev Keycloak over http; options validation rejects it elsewhere.
    public bool RequireHttpsMetadata { get; set; } = true;

    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;

    // Advisory acr_values for the step-up challenge; the access check itself stays amr-based.
    public string? StepUpAcrValues { get; set; }

    // Asymmetric only: an HMAC algorithm would let anyone holding the secret mint tokens.
    public static readonly IReadOnlyList<string> AllowedSigningAlgorithms =
    [
        "RS256", "RS384", "RS512",
        "PS256", "PS384", "PS512",
        "ES256", "ES384", "ES512",
    ];
}
