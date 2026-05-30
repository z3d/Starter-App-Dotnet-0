
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
}
