using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.OwnsOne(p => p.Price, priceBuilder =>
        {
            priceBuilder.Property(m => m.Amount)
                .HasColumnName("PriceAmount")
                .HasPrecision(18, 2);
            priceBuilder.Property(m => m.Currency)
                .HasColumnName("PriceCurrency")
                .HasMaxLength(3);
        });

        builder.Property(p => p.Name)
            .HasMaxLength(Product.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(Product.MaxDescriptionLength);
    }
}
