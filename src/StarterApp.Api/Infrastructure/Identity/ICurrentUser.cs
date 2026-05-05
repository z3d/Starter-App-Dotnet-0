namespace StarterApp.Api.Infrastructure.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string Subject { get; }

    AuthenticatedPrincipalType PrincipalType { get; }

    string TenantId { get; }

    IReadOnlySet<string> Scopes { get; }

    string CorrelationId { get; }

    string? Email { get; }

    string? ClientId { get; }

    string? Issuer { get; }

    bool HasScope(string scope);
}
