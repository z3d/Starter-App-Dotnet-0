using System.Text.Json.Serialization;

namespace StarterApp.Api.Infrastructure.Identity;

internal sealed class GatewayAssertionPayload
{
    [JsonPropertyName("iss")]
    public string Issuer { get; init; } = string.Empty;

    [JsonPropertyName("aud")]
    public string Audience { get; init; } = string.Empty;

    [JsonPropertyName("sub")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("pty")]
    public string PrincipalType { get; init; } = string.Empty;

    [JsonPropertyName("tid")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("scp")]
    public string[] Scopes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("cid")]
    public string CorrelationId { get; init; } = string.Empty;

    [JsonPropertyName("mth")]
    public string Method { get; init; } = string.Empty;

    [JsonPropertyName("pth")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("hsh")]
    public string HeaderHash { get; init; } = string.Empty;

    [JsonPropertyName("iat")]
    public long IssuedAt { get; init; }

    [JsonPropertyName("exp")]
    public long ExpiresAt { get; init; }

    [JsonPropertyName("kid")]
    public string? KeyId { get; init; }
}
