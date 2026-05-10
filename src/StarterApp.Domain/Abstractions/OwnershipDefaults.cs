namespace StarterApp.Domain.Abstractions;

public static class OwnershipDefaults
{
    public const int MaxOwnerSubjectLength = 200;
    public const int MaxTenantIdLength = 100;
    public const string LegacyOwnerSubject = "legacy-owner";
    public const string LegacyTenantId = "legacy-tenant";

    public static void Validate(string ownerSubject, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (ownerSubject.Length > MaxOwnerSubjectLength)
            throw new ArgumentException($"Owner subject cannot exceed {MaxOwnerSubjectLength} characters", nameof(ownerSubject));

        if (tenantId.Length > MaxTenantIdLength)
            throw new ArgumentException($"Tenant id cannot exceed {MaxTenantIdLength} characters", nameof(tenantId));
    }
}
