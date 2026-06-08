
namespace StarterApp.Tests.Infrastructure.Persistence;

public class PersistenceRegistrationTests
{
    [Fact]
    public void AddPersistence_EnablesRetryOnFailure_ForTransientPostgresFaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Host=localhost;Database=test;Username=postgres;Password=postgres");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();

        Assert.True(strategy.RetriesOnFailure,
            "EnableRetryOnFailure must be configured — PostgreSQL failovers and transient network faults should be retried.");
    }

    [Fact]
    public void AddPersistence_RegistersIDbConnection_AsTransient()
    {
        // Transient (not scoped): each query handler resolves its own NpgsqlConnection, so a
        // Task.WhenAll over two query handlers in one request can't collide on a single connection
        // (Npgsql has no MARS). Locks in the fix; a scoped registration must fail this.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Host=localhost;Database=test;Username=postgres;Password=postgres");

        var descriptor = services.Single(d => d.ServiceType == typeof(System.Data.IDbConnection));

        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }
}
