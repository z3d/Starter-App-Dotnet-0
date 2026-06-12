namespace StarterApp.Api.Infrastructure.Identity;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string Subject { get; }

    AuthenticatedPrincipalType PrincipalType { get; }

    string TenantId { get; }

    IReadOnlySet<string> Scopes { get; }

    IReadOnlySet<string> AuthenticationMethods { get; }

    string CorrelationId { get; }




    bool HasScope(string scope);

    bool HasAuthenticationMethod(string authenticationMethod);
}
