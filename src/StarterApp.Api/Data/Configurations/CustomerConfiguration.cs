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

        // No (tenant_id, owner_subject) index: the unique (tenant_id, owner_subject, email) index
        // below serves that prefix (0005_DropRedundantIndexes.sql).

        // The unique index ix_customers_tenant_id_owner_subject_email lives only in the DbUp
        // baseline migration. EF cannot declare an index that spans Customer and its owned Email
        // type, even though they share a table, so HasIndex("...", "Email.Value") fails.
        // The create and update customer handlers catch that constraint name when two requests
        // race to insert the same email.
    }
}
