using StarterApp.Api.Infrastructure.Outbox;

namespace StarterApp.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CaptureDomainEventsIntoOutbox();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CaptureDomainEventsIntoOutbox();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    // Single-SaveChanges outbox pattern.
    //
    // Aggregates raising creation events (via RecordCreation override) must assign their Id
    // client-side (e.g. Guid.CreateVersion7) so the event payload can be built BEFORE SaveChanges.
    // Enforced by DomainConventionTests.AggregatesOverridingRecordCreation_MustHaveGuidId.
    //
    // Why this matters: EF's retrying execution strategy (EnableRetryOnFailure) rejects user-initiated
    // transactions, and wrapping a two-SaveChanges flow in CreateExecutionStrategy().ExecuteAsync
    // is unsafe — mid-flow retry leaves the ChangeTracker out of sync with the rolled-back DB.
    // Collapsing to a single SaveChanges lets EF manage its own retry-aware transaction.
    private void CaptureDomainEventsIntoOutbox()
    {
        var newAggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToList();

        foreach (var aggregate in newAggregates)
            aggregate.RecordCreation();

        var aggregatesWithEvents = ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        if (aggregatesWithEvents.Count == 0)
            return;

        var outboxMessages = aggregatesWithEvents
            .SelectMany(aggregate => aggregate.DomainEvents)
            .Select(OutboxMessage.Create)
            .ToList();

        if (outboxMessages.Count > 0)
            OutboxMessages.AddRange(outboxMessages);

        // Clear now — if SaveChanges throws, the caller retries at the use-case layer; a second pass
        // would otherwise duplicate the outbox rows. Aggregate state is still dirty in the tracker.
        foreach (var aggregate in aggregatesWithEvents)
            aggregate.ClearDomainEvents();
    }
}
