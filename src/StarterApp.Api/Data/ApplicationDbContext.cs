using Microsoft.EntityFrameworkCore.Storage;
using StarterApp.Api.Infrastructure.Outbox;
using StarterApp.Domain.Events;

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
        return SaveChangesWithOutboxAsync(acceptAllChangesOnSuccess, sync: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public override int SaveChanges()
    {
        return SaveChanges(acceptAllChangesOnSuccess: true);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        return SaveChangesWithOutboxAsync(acceptAllChangesOnSuccess, sync: false, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Money value object to match the database columns in TestFixture
        modelBuilder.Entity<Product>()
            .OwnsOne(p => p.Price, priceBuilder =>
            {
                priceBuilder.Property(m => m.Amount)
                    .HasColumnName("PriceAmount")
                    .HasPrecision(18, 2);  // Add precision and scale
                priceBuilder.Property(m => m.Currency)
                    .HasColumnName("PriceCurrency")
                    .HasMaxLength(3);
            });

        modelBuilder.Entity<Product>()
            .Property(p => p.Name)
            .HasMaxLength(Product.MaxNameLength)
            .IsRequired();

        modelBuilder.Entity<Product>()
            .Property(p => p.Description)
            .HasMaxLength(Product.MaxDescriptionLength);

        // Configure Email value object for Customer
        modelBuilder.Entity<Customer>()
            .OwnsOne(c => c.Email, emailBuilder =>
            {
                emailBuilder.Property(e => e.Value)
                    .HasColumnName("Email")
                    .HasMaxLength(320);
            });

        modelBuilder.Entity<Customer>()
            .Property(c => c.Name)
            .HasMaxLength(Customer.MaxNameLength)
            .IsRequired();

        // Configure Order entity
        modelBuilder.Entity<Order>(orderBuilder =>
        {
            orderBuilder.HasKey(o => o.Id);

            // Configure OrderStatus enum as string
            orderBuilder.Property(o => o.Status)
                .HasConversion<string>();

            // Configure Items navigation via backing field (_items).
            // EF Core sets OrderId on each item when the Order is saved.
            orderBuilder.HasMany(o => o.Items)
                .WithOne()
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            orderBuilder.Navigation(o => o.Items)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Configure OrderItem entity
        modelBuilder.Entity<OrderItem>(itemBuilder =>
        {
            itemBuilder.ToTable("OrderItems");
            itemBuilder.HasKey(oi => oi.Id);

            itemBuilder.Property(oi => oi.ProductName)
                .HasMaxLength(100)
                .IsRequired();

            itemBuilder.Property(oi => oi.GstRate)
                .HasPrecision(5, 4);

            // Configure Money value object for UnitPriceExcludingGst (like Product.Price)
            itemBuilder.OwnsOne(oi => oi.UnitPriceExcludingGst, priceBuilder =>
            {
                priceBuilder.Property(m => m.Amount)
                    .HasColumnName("UnitPriceExcludingGst")
                    .HasPrecision(18, 2);
                priceBuilder.Property(m => m.Currency)
                    .HasColumnName("Currency")
                    .HasMaxLength(3);
            });

            // Configure relationship to Product
            itemBuilder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(oi => oi.ProductId);
        });

        modelBuilder.Entity<OutboxMessage>(outboxBuilder =>
        {
            outboxBuilder.ToTable("OutboxMessages");
            outboxBuilder.HasKey(message => message.Id);

            outboxBuilder.Property(message => message.Type)
                .HasMaxLength(200)
                .IsRequired();

            outboxBuilder.Property(message => message.Payload)
                .IsRequired();

            outboxBuilder.Property(message => message.RetryCount)
                .HasDefaultValue(0);

            outboxBuilder.Property(message => message.Error);

            outboxBuilder.HasIndex(message => message.OccurredOnUtc)
                .HasFilter("[ProcessedOnUtc] IS NULL AND [Error] IS NULL");
        });
    }

    private async Task<int> SaveChangesWithOutboxAsync(
        bool acceptAllChangesOnSuccess,
        bool sync,
        CancellationToken cancellationToken)
    {
        RecordCreationEventsForNewOrders();

        var aggregatesWithEvents = ChangeTracker.Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var shouldManageTransaction = Database.IsRelational() && Database.CurrentTransaction == null;
        IDbContextTransaction? transaction = null;

        try
        {
            if (shouldManageTransaction)
            {
                transaction = sync
                    ? Database.BeginTransaction()
                    : await Database.BeginTransactionAsync(cancellationToken);
            }

            var rowsAffected = sync
                ? base.SaveChanges(acceptAllChangesOnSuccess)
                : await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            if (aggregatesWithEvents.Count > 0)
            {
                var outboxMessages = aggregatesWithEvents
                    .SelectMany(aggregate => aggregate.DomainEvents)
                    .Select(OutboxMessage.Create)
                    .ToList();

                if (outboxMessages.Count > 0)
                {
                    OutboxMessages.AddRange(outboxMessages);

                    if (sync)
                        base.SaveChanges(acceptAllChangesOnSuccess);
                    else
                        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
                }
            }

            if (transaction != null)
            {
                if (sync)
                    transaction.Commit();
                else
                    await transaction.CommitAsync(cancellationToken);
            }

            foreach (var aggregate in aggregatesWithEvents)
                aggregate.ClearDomainEvents();

            return rowsAffected;
        }
        catch
        {
            if (transaction != null)
            {
                if (sync)
                    transaction.Rollback();
                else
                    await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction != null)
            {
                if (sync)
                    transaction.Dispose();
                else
                    await transaction.DisposeAsync();
            }
        }
    }

    private void RecordCreationEventsForNewOrders()
    {
        var newOrders = ChangeTracker.Entries<Order>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .Where(order => order.DomainEvents.OfType<OrderCreatedDomainEvent>().Any() == false);

        foreach (var order in newOrders)
            order.RecordCreation();
    }
}
