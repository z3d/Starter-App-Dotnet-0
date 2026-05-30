using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.OwnsOne(c => c.Email, emailBuilder =>
        {
            emailBuilder.Property(e => e.Value)
                .HasColumnName("email")
                .HasMaxLength(320);
        });

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(Customer.MaxNameLength)
            .IsRequired();

        builder.Property(c => c.OwnerSubject)
            .HasColumnName("owner_subject")
            .HasMaxLength(OwnershipDefaults.MaxOwnerSubjectLength)
            .IsRequired();

        builder.Property(c => c.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(OwnershipDefaults.MaxTenantIdLength)
            .IsRequired();

        builder.Property(c => c.DateCreated)
            .HasColumnName("date_created");

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active");

        builder.HasIndex(c => new { c.TenantId, c.OwnerSubject })
            .HasDatabaseName("ix_customers_tenant_id_owner_subject");
    }
}
