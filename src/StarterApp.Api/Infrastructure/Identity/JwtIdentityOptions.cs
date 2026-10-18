using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure.Identity;

public sealed class JwtIdentityOptions
{
    public const string SectionName = "Identity";

    // Required outside Development and Testing.
    public string? Authority { get; set; }

    // Where to fetch discovery when that is not the public issuer address. The issuer is still Authority.
    public string? MetadataAddress { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    // False only for the local Keycloak over plain http. Options validation rejects false anywhere else.
    public bool RequireHttpsMetadata { get; set; } = true;

    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;

    // Sent as acr_values in the step-up challenge, as a hint to the client. The access check itself uses amr.
    public string? StepUpAcrValues { get; set; }

    // Asymmetric algorithms only. With HMAC, anyone who held the shared secret could mint tokens.
    public static readonly IReadOnlyList<string> AllowedSigningAlgorithms =
    [
        "RS256", "RS384", "RS512",
        "PS256", "PS384", "PS512",
        "ES256", "ES384", "ES512",
    ];
}
