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
            var user = Map(context.User);
            if (user != null)
                currentUserAccessor.Set(user);
        }

        await _next(context);
    }

    private static CurrentUser? Map(ClaimsPrincipal principal)
    {
        // Both sub and tid are required: owner scoping, cache keys, and rate-limit partitions
        // key on subject + tenant, so a token missing either maps to no identity (the scope
        // filter then 401s) rather than authenticating with an empty owner-scope component —
        // an IdP missing its tenant mapper must fail loudly, not stamp rows with "".
        var subject = principal.FindFirstValue("sub");
        var tenantId = principal.FindFirstValue("tid");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId))
            return null;

        // pty is an optional custom claim a deployer's IdP may stamp to mark non-user callers
        // (daemons, service accounts); anything else — including its absence — maps to User.
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
