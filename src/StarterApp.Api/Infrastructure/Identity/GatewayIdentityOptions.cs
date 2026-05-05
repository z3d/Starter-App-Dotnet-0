using System.ComponentModel.DataAnnotations;

namespace StarterApp.Api.Infrastructure.Identity;

public sealed class GatewayIdentityOptions
{
    public const string SectionName = "GatewayIdentity";

    [Required]
    public GatewayIdentityMode Mode { get; init; } = GatewayIdentityMode.Required;

    [Required]
    public string Issuer { get; init; } = "apim";

    [Required]
    public string Audience { get; init; } = "starterapp-api";

    public string? SigningKey { get; init; }

    public string? KeyId { get; init; }

    [Range(0, 300)]
    public int ClockSkewSeconds { get; init; } = 30;

    [Range(1, 600)]
    public int MaxTokenLifetimeSeconds { get; init; } = 120;
}
