using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.OwnsOne(p => p.Price, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("price_amount")
                .HasPrecision(18, 2);
            priceBuilder.Property(m => m.Currency)
                .HasColumnName("price_currency")
                .HasMaxLength(3);
        });

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(Product.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(Product.MaxDescriptionLength);

        builder.Property(p => p.OwnerSubject)
            .HasColumnName("owner_subject")
            .HasMaxLength(OwnershipDefaults.MaxOwnerSubjectLength)
            .IsRequired();

        builder.Property(p => p.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(OwnershipDefaults.MaxTenantIdLength)
            .IsRequired();

        builder.Property(p => p.Stock)
            .HasColumnName("stock");

        builder.Property(p => p.LastUpdated)
            .HasColumnName("last_updated");

        builder.Property(p => p.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(p => new { p.TenantId, p.OwnerSubject })
            .HasDatabaseName("ix_products_tenant_id_owner_subject");
    }
}
