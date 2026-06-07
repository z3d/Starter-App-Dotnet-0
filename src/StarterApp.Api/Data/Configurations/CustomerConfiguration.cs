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

        // NOTE: the per-owner unique email index `ix_customers_tenant_id_owner_subject_email`
        // (DbUp baseline 0001_CreatePostgresSchema.sql) is intentionally NOT mirrored here.
        // It spans `tenant_id`/`owner_subject` (Customer) plus `email` (the owned Email value
        // object, a separate EF entity type), and EF Core's fluent API cannot declare a composite
        // index across an entity and its owned type even when they share a table. DbUp owns the
        // schema, so the unique constraint is enforced in the database; the create/update customer
        // handlers catch that exact constraint name as the race-safe uniqueness net. Do not try to
        // add this via HasIndex("...","Email.Value") — EF rejects the owned-column reference.
    }
}
