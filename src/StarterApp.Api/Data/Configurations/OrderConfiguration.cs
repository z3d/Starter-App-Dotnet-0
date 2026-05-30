using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        // Order.Id is assigned client-side in the aggregate constructor (Guid v7).
        // EF must not attempt to generate a value for it.
        builder.Property(o => o.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(o => o.OrderDate)
            .HasColumnName("order_date");

        builder.Property(o => o.Status)
            .HasColumnName("status")
            .HasConversion<string>();

        builder.Property(o => o.OwnerSubject)
            .HasColumnName("owner_subject")
            .HasMaxLength(OwnershipDefaults.MaxOwnerSubjectLength)
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(OwnershipDefaults.MaxTenantIdLength)
            .IsRequired();

        builder.Property(o => o.LastUpdated)
            .HasColumnName("last_updated");

        builder.Property(o => o.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(o => new { o.TenantId, o.OwnerSubject })
            .HasDatabaseName("ix_orders_tenant_id_owner_subject");

        builder.HasIndex(o => new { o.TenantId, o.OwnerSubject, o.CustomerId })
            .HasDatabaseName("ix_orders_tenant_id_owner_subject_customer_id");

        builder.HasIndex(o => new { o.TenantId, o.OwnerSubject, o.Status })
            .HasDatabaseName("ix_orders_tenant_id_owner_subject_status");

        // Configure Items navigation via backing field (_items).
        // EF Core sets OrderId on each item when the Order is saved.
        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
