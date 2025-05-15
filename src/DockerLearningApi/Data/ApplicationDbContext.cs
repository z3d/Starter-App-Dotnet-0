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
}