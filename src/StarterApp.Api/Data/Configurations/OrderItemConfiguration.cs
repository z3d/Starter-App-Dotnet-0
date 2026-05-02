using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems");
        builder.HasKey(oi => oi.Id);

        builder.Property(oi => oi.ProductName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(oi => oi.GstRate)
            .HasPrecision(5, 4);

        builder.OwnsOne(oi => oi.UnitPriceExcludingGst, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("UnitPriceExcludingGst")
                .HasPrecision(18, 2);
            priceBuilder.Property(m => m.Currency)
                .HasColumnName("Currency")
                .HasMaxLength(3);
        });

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(oi => oi.ProductId);
    }
}
