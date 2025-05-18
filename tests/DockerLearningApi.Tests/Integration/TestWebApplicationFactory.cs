using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Respawn.Graph;
using System.Data.Common;
using System.Reflection;
using Testcontainers.MsSql;
using DbUp;
using DockerLearningApi.Data;
using DockerLearningApi.Infrastructure;
using Microsoft.Extensions.Logging;

namespace DockerLearningApi.Tests.Integration;

// Use IApiMarker instead of Program class
public class TestWebApplicationFactory : WebApplicationFactory<IApiMarker>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer;
    private DbConnection _dbConnection = null!;
    private Respawner _respawner = null!;
    private readonly ILogger<TestWebApplicationFactory> _logger;
    
    public string ConnectionString { get; private set; } = null!;

    public TestWebApplicationFactory()
    {
        // Create a logger factory and logger for cleaner test output
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<TestWebApplicationFactory>();
        
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

            // Add ApplicationDbContext using the test container
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                if (ConnectionString != null)
                {
                    options.UseSqlServer(ConnectionString);
                }
            });
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Starting SQL Server container for tests");
            await _sqlContainer.StartAsync();
            
            ConnectionString = _sqlContainer.GetConnectionString();
            _logger.LogInformation("SQL Server container started");
            
            // Apply migrations using DbUp
            var upgrader = DeployChanges.To
                .SqlDatabase(ConnectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.Load("DockerLearning.DbMigrator"))
                .WithTransaction()
                .LogToConsole()
                .Build();

            var result = upgrader.PerformUpgrade();
            
            if (!result.Successful)
            {
                _logger.LogError("Database migration failed: {Error}", result.Error);
                throw new Exception($"Database migration failed: {result.Error}");
            }

            // Set up connection for Respawn
            _dbConnection = new SqlConnection(ConnectionString);
            await _dbConnection.OpenAsync();
            
            // Initialize Respawn for database cleanup
            _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                SchemasToInclude = new[] { "dbo" },
                TablesToIgnore = new Table[] { new Table("__SchemaVersions") } // Ignore DbUp's version table
            });
            
            _logger.LogInformation("Test database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing test database");
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
            _logger.LogError(ex, "Error resetting database");
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
                _logger.LogInformation("Test container resources cleaned up");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during test cleanup");
        }
    }
}