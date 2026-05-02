using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StarterApp.Api.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.OwnsOne(c => c.Email, emailBuilder =>
        {
            emailBuilder.Property(e => e.Value)
                .HasColumnName("Email")
                .HasMaxLength(320);
        });

        builder.Property(c => c.Name)
            .HasMaxLength(Customer.MaxNameLength)
            .IsRequired();
    }
}
