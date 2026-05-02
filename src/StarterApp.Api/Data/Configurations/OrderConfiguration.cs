using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        // Order.Id is assigned client-side in the aggregate constructor (Guid v7).
        // EF must not attempt to generate a value for it.
        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.Status)
            .HasConversion<string>();

        builder.Property(o => o.RowVersion)
            .IsRowVersion();

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
