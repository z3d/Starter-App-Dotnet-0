using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id");

        builder.Property(message => message.OccurredOnUtc)
            .HasColumnName("occurred_on_utc");

        builder.Property(message => message.Type)
            .HasColumnName("type")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(message => message.Payload)
            .HasColumnName("payload")
            .IsRequired();

        builder.Property(message => message.ProcessedOnUtc)
            .HasColumnName("processed_on_utc");

        builder.Property(message => message.RetryCount)
            .HasColumnName("retry_count")
            .HasDefaultValue(0);

        builder.Property(message => message.Error)
            .HasColumnName("error");

        builder.Property(message => message.ProcessingId)
            .HasColumnName("processing_id")
            .IsConcurrencyToken();

        builder.Property(message => message.LockedUntilUtc)
            .HasColumnName("locked_until_utc");

        builder.Property(message => message.ReplayCount)
            .HasColumnName("replay_count")
            .HasDefaultValue(0);

        builder.Property(message => message.ReplayedOnUtc)
            .HasColumnName("replayed_on_utc");

        builder.HasIndex(message => new { message.OccurredOnUtc, message.LockedUntilUtc })
            .HasDatabaseName("ix_outbox_messages_claimable")
            .HasFilter("processed_on_utc IS NULL AND error IS NULL");
    }
}
