namespace StarterApp.Application.Infrastructure.Identity.Session;

public sealed class BrowserSessionResponse
{
    public string Subject { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public string Scope { get; set; } = string.Empty;

    public List<string> AuthenticationMethods { get; set; } = [];

    public DateTimeOffset? AccessExpiresAt { get; set; }

    public string? CsrfToken { get; set; }

    public int IdleMinutes { get; set; }
}
