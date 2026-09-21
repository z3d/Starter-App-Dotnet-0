namespace StarterApp.Tests.TestBuilders;

// Single construction path for owner-scoped domain entities in tests. The domain entities expose
// only owner-aware constructors (the no-owner convenience ctors were removed to close a multi-tenant
// footgun), so tests build through here, defaulting owner/tenant to the same sentinels the old
// ctors used (OwnershipDefaults.Legacy*) — keeping owner-scoped query/assertion behaviour identical.
internal static class TestEntities
{
    public static Order Order(int customerId) =>
        new(customerId, OwnershipDefaults.LegacyOwnerSubject, OwnershipDefaults.LegacyTenantId);

    public static Order Order(Guid id, int customerId) =>
        new(id, customerId, OwnershipDefaults.LegacyOwnerSubject, OwnershipDefaults.LegacyTenantId);

    public static Customer Customer(string name, Email email) =>
        new(name, email, OwnershipDefaults.LegacyOwnerSubject, OwnershipDefaults.LegacyTenantId);

    // Optional params replicate the former ProductBuilder's defaults-plus-override ergonomics
    // (e.g. TestEntities.Product(stock: -1)); the positional form matches the old new Product(...)
    // call sites. price defaults inside because Money.Create is not a compile-time constant.
    public static Product Product(
        string name = "Default Product",
        string? description = "Default Description",
        Money? price = null,
        int stock = 10) =>
        new(name, description, price ?? Money.Create(9.99m), stock,
            OwnershipDefaults.LegacyOwnerSubject, OwnershipDefaults.LegacyTenantId);
}
