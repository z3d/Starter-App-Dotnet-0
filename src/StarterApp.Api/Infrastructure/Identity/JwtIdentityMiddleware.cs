using System.Security.Claims;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Identity;

// The single writer of ICurrentUser; nothing downstream reads HttpContext.User (convention-enforced).
internal sealed class JwtIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public JwtIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUserAccessor currentUserAccessor)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = Map(context.User);
            if (user != null)
                currentUserAccessor.Set(user);
        }

        await _next(context);
    }

    private static CurrentUser? Map(ClaimsPrincipal principal)
    {
        // A token missing sub or tid maps to no identity rather than stamping rows with an empty owner scope.
        var subject = principal.FindFirstValue("sub");
        var tenantId = principal.FindFirstValue("tid");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId))
            return null;

        // pty is an optional deployer claim marking non-user callers; anything else, including absence, is User.
        var principalType = string.Equals(principal.FindFirstValue("pty"), nameof(AuthenticatedPrincipalType.Service), StringComparison.Ordinal)
            ? AuthenticatedPrincipalType.Service
            : AuthenticatedPrincipalType.User;

        return new CurrentUser(
            subject,
            principalType,
            tenantId,
            ReadMultiValueClaim(principal, "scp", "scope"),
            ReadMultiValueClaim(principal, "amr"));
    }

    // Entra emits space-delimited scp, Keycloak scope; array claims arrive repeated or as one serialized array.
    private static IReadOnlyList<string> ReadMultiValueClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        var values = new List<string>();

        foreach (var claim in principal.Claims.Where(claim => claimTypes.Contains(claim.Type, StringComparer.Ordinal)))
        {
            if (claim.Value.StartsWith('['))
            {
                try
                {
                    values.AddRange(JsonSerializer.Deserialize<string[]>(claim.Value) ?? []);
                    continue;
                }
                catch (JsonException)
                {
                    // Not a JSON array after all — fall through to space-splitting.
                }
            }

            values.AddRange(claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return values;
    }
}
