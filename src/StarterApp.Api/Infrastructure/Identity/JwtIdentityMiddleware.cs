using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace StarterApp.Api.Infrastructure.Identity;

// The only place that sets ICurrentUser. Nothing after it reads HttpContext.User, and a convention test checks that.
internal sealed class JwtIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public JwtIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, CurrentUserAccessor currentUserAccessor)
    {
        var user = context.User.Identity?.IsAuthenticated == true ? Map(context.User) : null;
        if (user is null)
        {
            await _next(context);
            return;
        }

        currentUserAccessor.Set(user);
        Activity.Current?.SetTag("tenant.id", user.TenantId);
        using (Serilog.Context.LogContext.PushProperty("TenantId", user.TenantId))
            await _next(context);
    }

    private static CurrentUser? Map(ClaimsPrincipal principal)
    {
        // A token without sub or tid gives no identity at all, rather than rows stamped with an empty owner.
        var subject = principal.FindFirstValue("sub");
        var tenantId = principal.FindFirstValue("tid");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenantId))
            return null;

        // pty is an optional claim the deployer can add to mark a service caller. Anything else, including no claim, means User.
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

    // Entra sends scopes as a space-separated scp claim, Keycloak as scope. An array claim arrives either repeated or as one JSON array.
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
                }
            }

            values.AddRange(claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return values;
    }
}
