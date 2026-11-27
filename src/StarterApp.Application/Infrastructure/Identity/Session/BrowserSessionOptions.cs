using System.ComponentModel.DataAnnotations;

namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class BrowserSessionOptions
{
    public const string SectionName = "Identity:Session";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    // The web app's own origin, e.g. https://app.example.com: the sign-in returns to it and the API never trusts a forwarded host for it.
    public string? WebOrigin { get; set; }

    public string Scope { get; set; } = "openid";

    [Range(1, 1440)]
    public int IdleMinutes { get; set; } = 30;

    [Range(1, 72)]
    public int MaxHours { get; set; } = 10;

    [Range(0, 600)]
    public int RefreshBeforeExpirySeconds { get; set; } = 60;

    // Auth0 only: the API identifier and the Organization; empty for a Keycloak realm.
    public string? Audience { get; set; }

    public string? Organization { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId);

    public IReadOnlyList<string> Scopes =>
        Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
