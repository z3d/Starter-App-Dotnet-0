using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

public class ApplicationDbContext : DbContext
{
    // Domain-event capture into the outbox lives in DomainEventsInterceptor (attached by AddPersistence),
    // not here: the context owns persistence only. A context constructed without the interceptor
    // (design-time tooling, model-only tests) simply does not capture.
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
