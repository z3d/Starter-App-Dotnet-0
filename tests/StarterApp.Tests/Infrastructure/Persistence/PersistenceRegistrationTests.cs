using Microsoft.Extensions.Hosting;


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

    [Fact]
    public void AddPersistence_GivesEfCoreAndDapper_TheSameDataSource()
    {
        // One NpgsqlDataSource per process is what lets the managed-identity password provider be
        // configured once and still cover both the EF Core context and the Dapper connections.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Host=localhost;Database=test;Username=postgres;Password=postgres");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var dapperConnection = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>();

        Assert.Same(dataSource, provider.GetRequiredService<NpgsqlDataSource>());
        Assert.Equal(dataSource.ConnectionString, dbContext.Database.GetConnectionString());
        Assert.Equal(dataSource.ConnectionString, dapperConnection.ConnectionString);
    }

    [Fact]
    public void AddDatabaseDataSource_RegistersOnce_WhoeverAsksFirst()
    {
        // AddPersistence (API) and AddJobRunRecording (ServiceDefaults, also used by Functions)
        // both call it; the process must end up with one data source, so the managed-identity
        // password provider is configured once and every connection carries the token.
        var services = new ServiceCollection();
        services.AddDatabaseDataSource("Host=localhost;Database=test;Username=postgres;Password=postgres");
        services.AddDatabaseDataSource("Host=localhost;Database=other;Username=postgres;Password=postgres");

        var descriptors = services.Where(d => d.ServiceType == typeof(NpgsqlDataSource)).ToList();

        Assert.Single(descriptors);
        Assert.Equal(ServiceLifetime.Singleton, descriptors[0].Lifetime);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("test", provider.GetRequiredService<NpgsqlDataSource>().ConnectionString.Split("Database=")[1].Split(';')[0]);
    }

    [Fact]
    public void AddPersistence_AndAddJobRunRecording_ShareTheDataSource()
    {
        const string connectionString = "Host=localhost;Database=test;Username=postgres;Password=postgres";
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:database"] = connectionString;
        builder.Services.AddPersistence(connectionString);
        builder.AddJobRunRecording();

        using var host = builder.Build();
        var dataSource = host.Services.GetRequiredService<NpgsqlDataSource>();
        var recorder = host.Services.GetRequiredService<IJobRunRecorder>();

        Assert.Single(builder.Services, d => d.ServiceType == typeof(NpgsqlDataSource));
        Assert.IsType<NpgsqlJobRunRecorder>(recorder);
        Assert.Same(dataSource, host.Services.GetRequiredService<NpgsqlDataSource>());
    }
}
