using DockerLearningApi.Models;
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
        // Seed some sample data
        modelBuilder.Entity<Product>().HasData(
            new Product { Id = 1, Name = "Product 1", Description = "Description for product 1", Price = 10.99m, Stock = 100 },
            new Product { Id = 2, Name = "Product 2", Description = "Description for product 2", Price = 24.99m, Stock = 50 },
            new Product { Id = 3, Name = "Product 3", Description = "Description for product 3", Price = 5.99m, Stock = 200 }
        );
    }
}