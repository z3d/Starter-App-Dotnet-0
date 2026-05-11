using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(message => message.CorrelationId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(message => message.Payload)
            .IsRequired();

        builder.Property(message => message.RetryCount)
            .HasDefaultValue(0);

        builder.Property(message => message.Error);

        builder.Property(message => message.ProcessingId)
            .IsConcurrencyToken();

        builder.Property(message => message.LockedUntilUtc);

        builder.HasIndex(message => new { message.OccurredOnUtc, message.LockedUntilUtc })
            .HasDatabaseName("IX_OutboxMessages_Claimable")
            .HasFilter("[ProcessedOnUtc] IS NULL AND [Error] IS NULL");
    }
}
