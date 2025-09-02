using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;
using StarterApp.Api.Data;
using StarterApp.Api.Infrastructure;
using System.Data.Common;
using Testcontainers.MsSql;

namespace StarterApp.Tests.Integration;

// Use IApiMarker instead of Program class
public class TestWebApplicationFactory : WebApplicationFactory<IApiMarker>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer;
    private DbConnection _dbConnection = null!;
    private Respawner _respawner = null!;

    public string ConnectionString { get; private set; } = null!;

    public TestWebApplicationFactory()
    {
        // Configure Serilog for tests
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        // Use a secure password with config (this could be fetched from user secrets in a real app)
        var containerPassword = "TestContainer!Password123";

        _sqlContainer = new MsSqlBuilder()
            .WithPassword(containerPassword)
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configBuilder =>
        {
            // Only set connection string if it has been initialized
            if (ConnectionString != null)
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = ConnectionString
                });
            }
        });

        builder.ConfigureServices(services =>
        {
            // Remove the app's ApplicationDbContext registration
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove the app's IDbConnection registration
            var dbConnectionDescriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(System.Data.IDbConnection));

            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            // Add ApplicationDbContext using the test container
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (ConnectionString != null)
                {
                    options.UseSqlServer(ConnectionString);
                }
            });

            // Add IDbConnection for Dapper using the test container
            services.AddScoped<System.Data.IDbConnection>(provider =>
                new Microsoft.Data.SqlClient.SqlConnection(ConnectionString ?? throw new InvalidOperationException("Connection string not set")));
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            Log.Information("Starting SQL Server container for tests");
            await _sqlContainer.StartAsync();

            ConnectionString = _sqlContainer.GetConnectionString();
            Log.Information("SQL Server container started");

            // Apply migrations using DbUp
            var upgrader = DeployChanges.To
                .SqlDatabase(ConnectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.Load("StarterApp.DbMigrator"))
                .WithTransaction()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();

            if (!result.Successful)
            {
                Log.Error("Database migration failed: {Error}", result.Error);
                throw new Exception($"Database migration failed: {result.Error}");
            }

            // Set up connection for Respawn
            _dbConnection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            await _dbConnection.OpenAsync();

            // Initialize Respawn for database cleanup
            _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                SchemasToInclude = new[] { "dbo" },
                TablesToIgnore = new Table[] { new Table("__SchemaVersions") } // Ignore DbUp's version table
            });

            Log.Information("Test database initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing test database");
            // Clean up container if initialization fails
            await _sqlContainer.DisposeAsync();
            throw;
        }
    }

    public async Task ResetDatabaseAsync()
    {
        try
        {
            await _respawner.ResetAsync(_dbConnection);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting database");
            throw;
        }
    }

    public new async Task DisposeAsync()
    {
        try
        {
            if (_dbConnection != null)
            {
                await _dbConnection.CloseAsync();
                await _dbConnection.DisposeAsync();
            }

            if (_sqlContainer != null)
            {
                await _sqlContainer.DisposeAsync();
                Log.Information("Test container resources cleaned up");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during test cleanup");
        }
    }
}



