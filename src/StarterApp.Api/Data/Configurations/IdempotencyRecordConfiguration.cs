using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterApp.Api.Infrastructure.Idempotency;

namespace StarterApp.Api.Data.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public const string PrimaryKeyName = "pk_idempotency_records";

    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(record => new { record.TenantId, record.OwnerSubject, record.Operation, record.Key })
            .HasName(PrimaryKeyName);

        builder.Property(record => record.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(OwnershipDefaults.MaxTenantIdLength);

        builder.Property(record => record.OwnerSubject)
            .HasColumnName("owner_subject")
            .HasMaxLength(OwnershipDefaults.MaxOwnerSubjectLength);

        builder.Property(record => record.Operation)
            .HasColumnName("operation")
            .HasMaxLength(100);

        builder.Property(record => record.Key)
            .HasColumnName("idempotency_key")
            .HasMaxLength(IdempotencyRecord.MaxKeyLength);

        builder.Property(record => record.RequestHash)
            .HasColumnName("request_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record => record.ResourceId)
            .HasColumnName("resource_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(record => record.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .HasDefaultValueSql("now()");
    }
}
