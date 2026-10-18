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

        builder.Property(c => c.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        // No index on (tenant_id, owner_subject). The unique index below starts with those columns and covers it.

        // The unique index on (tenant_id, owner_subject, email) is only in the DbUp baseline script. EF Core cannot build an index that includes the owned Email type.
    }
}
