using Testcontainers.MsSql;
using StarterApp.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Respawn;
using Respawn.Graph;
using System.Data.SqlClient;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Builders;
using System.Reflection;
using DbUp;
using DbUp.Engine;
using StarterApp.Api.Infrastructure; // Added for IApiMarker access
using Serilog;

namespace StarterApp.Tests.Integration;

// Custom wait strategy class
public class WaitUntil : IWaitUntil
{
    private readonly Func<IContainer, Task<bool>> _condition;
    private readonly TimeSpan _interval;

    public WaitUntil(Func<IContainer, Task<bool>> condition, TimeSpan interval)
    {
        _condition = condition;
        _interval = interval;
    }

    public async Task<bool> UntilAsync(IContainer container)
    {
        return await _condition(container);
    }

    public TimeSpan Interval => _interval;
}

public class TestDatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer;
    public string ConnectionString { get; private set; } = null!;
    private Respawner _respawner = null!;
    private const string DbName = "TestDb";
    private const string DbPassword = "Password@123";
    
    public TestDatabaseFixture()
    {
        // Configure SQL Server container with more specific settings
        _sqlContainer = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithPassword(DbPassword)
            .WithPortBinding(1433, true) // Use a random port to avoid conflicts
            .WithEnvironment("ACCEPT_EULA", "Y")
            .WithEnvironment("MSSQL_PID", "Developer")
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilPortIsAvailable(1433)
                    .AddCustomWaitStrategy(new WaitUntil(
                        async (container) => {
                            try {
                                // Try to connect to the database directly
                                var connString = $"Server=127.0.0.1,{container.GetMappedPublicPort(1433)};Database=master;User Id=sa;Password={DbPassword};TrustServerCertificate=True;Connection Timeout=5";
                                using var connection = new SqlConnection(connString);
                                await connection.OpenAsync();
                                using var cmd = new SqlCommand("SELECT 1", connection);
                                await cmd.ExecuteScalarAsync();
                                return true;
                            } catch {
                                // If connection fails, container is not ready
                                return false;
                            }
                        },
                        TimeSpan.FromSeconds(2) // Check every 2 seconds
                    ))
            )
            .Build();
    }
    
    public async Task InitializeAsync()
    {
        try
        {
            Console.WriteLine("Starting SQL Server container...");
            // Start SQL Server container
            await _sqlContainer.StartAsync();
            Console.WriteLine($"SQL Server container started on port {_sqlContainer.GetMappedPublicPort(1433)}");
            
            // Get base connection string first (points to master database)
            var masterConnectionString = $"Server=127.0.0.1,{_sqlContainer.GetMappedPublicPort(1433)};Database=master;User Id=sa;Password={DbPassword};TrustServerCertificate=True";
            Console.WriteLine($"Master connection string: {masterConnectionString}");
            
            // Create a test database
            using var connection = new SqlConnection(masterConnectionString);
            await connection.OpenAsync();
            var createDbCommand = new SqlCommand($"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{DbName}') CREATE DATABASE {DbName}", connection);
            await createDbCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Created test database '{DbName}'");
            
            // Build connection string to the test database
            ConnectionString = masterConnectionString.Replace("Database=master", $"Database={DbName}");
            Console.WriteLine($"Using connection string: {ConnectionString}");
            
            // Run DbUp migrations on the test database
            Console.WriteLine("Applying database migrations using DbUp...");
            if (!RunDbUpMigrations(ConnectionString))
            {
                throw new Exception("Failed to apply database migrations");
            }
            Console.WriteLine("Database migrations applied successfully");
            
            // Initialize Respawner for cleaning database between tests
            await InitializeRespawner();
            Console.WriteLine("Respawner initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing test database: {ex}");
            throw;
        }
    }
    
    private bool RunDbUpMigrations(string connectionString)
    {
        try
        {
            // Use DbUp to run migrations from the DbMigrator project
            var migratorAssembly = Assembly.Load("StarterApp.DbMigrator");
            
            var upgrader = DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(migratorAssembly) // Use embedded scripts from the DbMigrator assembly
                .LogToConsole()
                .WithTransaction()
                .Build();

            var result = upgrader.PerformUpgrade();
            
            if (!result.Successful)
            {
                Console.WriteLine($"Database migration failed: {result.Error}");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Migration exception: {ex}");
            return false;
        }
    }
    
    public async Task DisposeAsync()
    {
        try
        {
            await _sqlContainer.DisposeAsync();
            Console.WriteLine("SQL Server container disposed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disposing SQL container: {ex}");
        }
    }
    
    private async Task InitializeRespawner()
    {
        try
        {
            using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.SqlServer,
                SchemasToInclude = new[] { "dbo" },
                TablesToIgnore = new Table[] { new Table("SchemaVersions") } // DbUp's version tracking table
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing Respawner: {ex}");
            throw;
        }
    }
    
    public async Task ResetDatabaseAsync()
    {
        try
        {
            Console.WriteLine("Resetting database for test");
            using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            
            // We don't need to manually handle SchemaVersions now
            // DbUp manages it, and we configured Respawner to ignore it
            await _respawner.ResetAsync(connection);
            
            Console.WriteLine("Database reset complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resetting database: {ex}");
            throw;
        }
    }
}

// Make ApiTestFixture have a parameterless constructor for xUnit and proper connection string sharing
public class ApiTestFixture : WebApplicationFactory<IApiMarker>, IAsyncLifetime
{
    private readonly TestDatabaseFixture _dbFixture;
    private readonly Serilog.ILogger _logger;
    public HttpClient Client { get; private set; } = null!;
    
    // Expose the connection string from the database fixture
    public string ConnectionString => _dbFixture.ConnectionString;
    
    // Parameterless constructor for xUnit collection fixture
    public ApiTestFixture()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
        
        _dbFixture = new TestDatabaseFixture();
    }    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The connection string is set as an environment variable in InitializeAsync
        // before this method is called, so Program.cs should find it successfully
        
        builder.UseEnvironment("Testing");
        
        builder.ConfigureLogging(logging => 
        {
            logging.ClearProviders();
            logging.AddSerilog(_logger);
        });
        
        builder.ConfigureTestServices(services =>
        {
            // Any test-specific service overrides can go here
        });
    }
      public async Task InitializeAsync()
    {
        try
        {
            // Initialize the database fixture first to get the connection string
            Log.Information("Initializing database fixture");
            await _dbFixture.InitializeAsync();
            
            // Set the connection string as environment variable before creating the client
            // This ensures Program.cs can find it during WebApplicationFactory host creation
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _dbFixture.ConnectionString);
            Log.Information($"Set connection string environment variable: {_dbFixture.ConnectionString}");
            
            // Then create the client with the configured web host
            Client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            Log.Information("Test API client created and ready");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing ApiTestFixture");
            throw;
        }
    }
      // Add 'new' keyword to hide the inherited member
    public new async Task DisposeAsync()
    {
        try 
        {
            // Clean up environment variable
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            
            await _dbFixture.DisposeAsync();
            Log.Information("ApiTestFixture disposed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing ApiTestFixture");
        }
    }
    
    public async Task ResetDatabaseAsync()
    {
        try
        {
            await _dbFixture.ResetDatabaseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting database in ApiTestFixture");
            throw;
        }
    }
}

// Fix the test collection definition
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<ApiTestFixture>
{
    // This class has no code, and is never created. Its purpose is to be the place
    // to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}