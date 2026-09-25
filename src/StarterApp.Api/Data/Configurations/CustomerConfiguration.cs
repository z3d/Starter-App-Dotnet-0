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

        builder.Property(c => c.LastUpdated)
            .HasColumnName("last_updated");

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active");

        // No (tenant_id, owner_subject) index: the unique index below serves that prefix.

        // The unique (tenant_id, owner_subject, email) index lives only in the DbUp baseline: EF cannot index across the owned Email type.
    }
}
