using DockerLearning.Domain.Entities;
using DockerLearning.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DockerLearningApi.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Money value object as owned entity
        modelBuilder.Entity<Product>()
            .OwnsOne(p => p.Price, priceBuilder =>
            {
                priceBuilder.Property(m => m.Amount).HasColumnName("Price_Amount");
                priceBuilder.Property(m => m.Currency).HasColumnName("Price_Currency").HasMaxLength(3);
            });
    }
}