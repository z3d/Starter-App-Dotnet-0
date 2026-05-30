using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");
        builder.HasKey(oi => oi.Id);

        builder.Property(oi => oi.Id)
            .HasColumnName("id");

        builder.Property(oi => oi.OrderId)
            .HasColumnName("order_id");

        builder.Property(oi => oi.ProductId)
            .HasColumnName("product_id");

        builder.Property(oi => oi.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(oi => oi.Quantity)
            .HasColumnName("quantity");

        builder.Property(oi => oi.GstRate)
            .HasColumnName("gst_rate")
            .HasPrecision(5, 4);

        builder.OwnsOne(oi => oi.UnitPriceExcludingGst, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("unit_price_excluding_gst")
                .HasPrecision(18, 2);
            priceBuilder.Property(m => m.Currency)
                .HasColumnName("currency")
                .HasMaxLength(3);
        });

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(oi => oi.ProductId);
    }
}
