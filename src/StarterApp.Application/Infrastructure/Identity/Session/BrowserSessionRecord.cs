namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class BrowserSessionRecord
{
    public string? AccessToken { get; set; }

    public string? RefreshToken { get; set; }

    public string? IdToken { get; set; }

    public DateTimeOffset AccessTokenExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public string CsrfToken { get; set; } = string.Empty;

    // Set when the idle or absolute limit ends the session; the id token stays so sign-out can still end it at the provider.
    public string? EndedBecause { get; set; }
}
