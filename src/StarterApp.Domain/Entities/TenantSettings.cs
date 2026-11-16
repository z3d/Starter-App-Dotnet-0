using System.Text.RegularExpressions;

namespace StarterApp.Domain.Entities;

public partial class TenantSettings
{
    public const int MaxPaletteLength = 32;

    public string TenantId { get; private set; } = string.Empty;
    public string Palette { get; private set; } = string.Empty;
    public string UpdatedBy { get; private set; } = string.Empty;
    public DateTimeOffset DateCreated { get; private set; }
    public DateTimeOffset LastUpdated { get; private set; }
    public uint RowVersion { get; private set; }

    protected TenantSettings()
    {
    }

    public TenantSettings(string tenantId, string palette, string updatedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (tenantId.Length > OwnershipDefaults.MaxTenantIdLength)
            throw new ArgumentException($"Tenant id cannot exceed {OwnershipDefaults.MaxTenantIdLength} characters", nameof(tenantId));

        TenantId = tenantId;
        ChoosePalette(palette, updatedBy);
    }

    public void ChoosePalette(string palette, string updatedBy)
    {
        if (!IsPaletteName(palette))
            throw new ArgumentException("A palette is a lower-case name of letters, digits and hyphens", nameof(palette));
        ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);
        if (updatedBy.Length > OwnershipDefaults.MaxOwnerSubjectLength)
            throw new ArgumentException($"Subject cannot exceed {OwnershipDefaults.MaxOwnerSubjectLength} characters", nameof(updatedBy));

        Palette = palette;
        UpdatedBy = updatedBy;
    }

    public static bool IsPaletteName(string? palette) => palette is not null && PaletteName().IsMatch(palette);

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,31}\z")]
    private static partial Regex PaletteName();
}
