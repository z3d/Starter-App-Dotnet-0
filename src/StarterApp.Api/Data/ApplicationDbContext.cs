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

        // Configure Email value object for Customer
        modelBuilder.Entity<Customer>()
            .OwnsOne(c => c.Email, emailBuilder =>
            {
                emailBuilder.Property(e => e.Value)
                    .HasColumnName("Email")
                    .HasMaxLength(320);
            });

        // Configure Order entity
        modelBuilder.Entity<Order>(orderBuilder =>
        {
            orderBuilder.HasKey(o => o.Id);

            // Configure OrderStatus enum as string
            orderBuilder.Property(o => o.Status)
                .HasConversion<string>();

            // Configure the Items collection relationship
            orderBuilder.HasMany<OrderItem>()
                .WithOne()
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure the Items as ignored since EF will load via relationship
            orderBuilder.Ignore(o => o.Items);
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
    }
}



