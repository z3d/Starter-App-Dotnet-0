using Microsoft.Extensions.DependencyInjection;
using StarterApp.Api.Data;
using StarterApp.Api.Infrastructure;

namespace StarterApp.Tests.Infrastructure.Persistence;

public class PersistenceRegistrationTests
{
    [Fact]
    public void AddPersistence_EnablesRetryOnFailure_ForTransientAzureSqlFaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Server=.;Database=Test;Integrated Security=true;TrustServerCertificate=true");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var strategy = dbContext.Database.CreateExecutionStrategy();

        Assert.True(strategy.RetriesOnFailure,
            "EnableRetryOnFailure must be configured — Azure SQL throttling and failover cause transient connection errors.");
    }
}
