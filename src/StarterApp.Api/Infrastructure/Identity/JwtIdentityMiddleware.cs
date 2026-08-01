using System.Security.Claims;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Identity;

// The single writer of ICurrentUser: maps the claims principal produced by the JWT bearer
// handler onto the scoped CurrentUserAccessor. Everything downstream (handlers, owner policy,
// cache keys, rate limiting, audit stamping) reads ICurrentUser and never touches
// HttpContext.User — convention-enforced, so claim-shape differences between IdPs stay
// contained here.
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
            var user = Map(context.User, context.TraceIdentifier);
            if (user != null)
                currentUserAccessor.Set(user);
        }

        await _next(context);
    }

    private static CurrentUser? Map(ClaimsPrincipal principal, string correlationId)
    {
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(subject))
            return null;

        var principalType = string.Equals(principal.FindFirstValue("pty"), nameof(AuthenticatedPrincipalType.Service), StringComparison.Ordinal)
            ? AuthenticatedPrincipalType.Service
            : AuthenticatedPrincipalType.User;

        return new CurrentUser(
            subject,
            principalType,
            principal.FindFirstValue("tid") ?? string.Empty,
            ReadMultiValueClaim(principal, "scp", "scope"),
            correlationId,
            ReadMultiValueClaim(principal, "amr"));
    }

    // Claim shapes differ by IdP: Entra emits space-delimited "scp", Keycloak a space-delimited
    // "scope"; JSON-array claims (Keycloak's amr) surface either as repeated claims or as a
    // single claim whose value is the serialized array. Normalize all of them.
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
