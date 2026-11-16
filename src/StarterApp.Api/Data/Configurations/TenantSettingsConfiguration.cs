using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class TenantSettingsConfiguration : IEntityTypeConfiguration<TenantSettings>
{
    public void Configure(EntityTypeBuilder<TenantSettings> builder)
    {
        builder.ToTable("tenant_settings");
        builder.HasKey(s => s.TenantId);

        builder.Property(s => s.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(OwnershipDefaults.MaxTenantIdLength)
            .ValueGeneratedNever();

        builder.Property(s => s.Palette)
            .HasColumnName("palette")
            .HasMaxLength(TenantSettings.MaxPaletteLength)
            .IsRequired();

        builder.Property(s => s.UpdatedBy)
            .HasColumnName("updated_by")
            .HasMaxLength(OwnershipDefaults.MaxOwnerSubjectLength)
            .IsRequired();

        builder.Property(s => s.DateCreated)
            .HasColumnName("date_created");

        builder.Property(s => s.LastUpdated)
            .HasColumnName("last_updated");

        builder.Property(s => s.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();
    }
}
