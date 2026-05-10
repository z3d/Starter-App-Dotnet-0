namespace StarterApp.Tests;

internal static class TestOwnerOnlyPolicy
{
    public static IOwnerOnlyPolicy Instance { get; } = new OwnerOnlyPolicy(new CurrentUser(
        OwnershipDefaults.LegacyOwnerSubject,
        AuthenticatedPrincipalType.User,
        OwnershipDefaults.LegacyTenantId,
        ["customers:read", "customers:write", "orders:read", "orders:write", "products:read", "products:write"],
        "test-correlation",
        null,
        null,
        null));

    public static IOwnerOnlyPolicy For(string subject, string tenantId)
    {
        return new OwnerOnlyPolicy(new CurrentUser(
            subject,
            AuthenticatedPrincipalType.User,
            tenantId,
            ["customers:read", "customers:write", "orders:read", "orders:write", "products:read", "products:write"],
            "test-correlation",
            null,
            null,
            null));
    }
}
