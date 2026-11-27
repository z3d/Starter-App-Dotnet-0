namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed record IdentityProviderTokens(string AccessToken, string? RefreshToken, string? IdToken, int ExpiresInSeconds);
