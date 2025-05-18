using Testcontainers.MsSql;
using DockerLearningApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Respawn;
using Respawn.Graph;
using System.Data.SqlClient;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Builders;
using System.Reflection;
using DbUp;
using DbUp.Engine;

namespace DockerLearningApi.Tests.Integration;

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
            using (var connection = new SqlConnection(masterConnectionString))
            {
                await connection.OpenAsync();
                var createDbCommand = new SqlCommand($"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{DbName}') CREATE DATABASE {DbName}", connection);
                await createDbCommand.ExecuteNonQueryAsync();
                Console.WriteLine($"Created test database '{DbName}'");
            }
            
            // Build connection string to the test database
            ConnectionString = masterConnectionString.Replace("Database=master", $"Database={DbName}");
            Console.WriteLine($"Using connection string: {ConnectionString}");
            
            // Run custom DbUp migrations on the test database instead of using DatabaseMigrator
            Console.WriteLine("Applying database migrations...");
            if (!RunTestMigrations(ConnectionString))
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
    
    private bool RunTestMigrations(string connectionString)
    {
        try
        {
            // Get reference to the API assembly where SQL scripts are embedded
            var apiAssembly = typeof(DockerLearningApi.Data.DatabaseMigrator).Assembly;
            
            // First, create the products table with a custom script
            string createTableScript = @"
                -- Create the Products table with LastUpdated column included from the start
                CREATE TABLE Products (
                    Id INT PRIMARY KEY IDENTITY(1,1),
                    Name NVARCHAR(100) NOT NULL,
                    Description NVARCHAR(500) NULL,
                    PriceAmount DECIMAL(18, 2) NOT NULL,
                    PriceCurrency NVARCHAR(3) NOT NULL DEFAULT 'USD',
                    Stock INT NOT NULL DEFAULT 0,
                    LastUpdated DATETIME2 NOT NULL DEFAULT GETUTCDATE()
                );
                
                -- Create SchemaVersions table to track migrations
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '__SchemaVersions')
                BEGIN
                    CREATE TABLE [dbo].[__SchemaVersions](
                        [Id] [int] IDENTITY(1,1) NOT NULL,
                        [ScriptName] [nvarchar](255) NOT NULL,
                        [Applied] [datetime] NOT NULL,
                        CONSTRAINT [PK_SchemaVersions_Id] PRIMARY KEY CLUSTERED ([Id] ASC)
                    )
                END
                
                -- Insert record to indicate the migrations have been applied
                INSERT INTO [__SchemaVersions] (ScriptName, Applied)
                VALUES 
                    ('DockerLearningApi.SqlScripts.0001_CreateProductsTable.sql', GETUTCDATE()),
                    ('DockerLearningApi.SqlScripts.0002_AddLastUpdatedColumn.sql', GETUTCDATE());
            ";
            
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand(createTableScript, connection))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine("Created products table with all required columns");
                }
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
                TablesToIgnore = new Table[] { new Table("__SchemaVersions") } // DbUp's version tracking table
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
            
            // More thorough approach: first truncate the SchemaVersions table instead of dropping it
            // This preserves the table structure but removes all rows
            using (var command = new SqlCommand(@"
                IF OBJECT_ID('dbo.__SchemaVersions', 'U') IS NOT NULL 
                BEGIN
                    TRUNCATE TABLE [dbo].[__SchemaVersions]
                    
                    -- Re-insert baseline migration records to keep DbUp happy
                    INSERT INTO [__SchemaVersions] (ScriptName, Applied)
                    VALUES 
                        ('DockerLearningApi.SqlScripts.0001_CreateProductsTable.sql', GETUTCDATE()),
                        ('DockerLearningApi.SqlScripts.0002_AddLastUpdatedColumn.sql', GETUTCDATE())
                END", connection))
            {
                await command.ExecuteNonQueryAsync();
            }
            
            // Then reset other tables
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
public class ApiTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private TestDatabaseFixture _dbFixture;
    public HttpClient Client { get; private set; } = null!;
    
    // Expose the connection string from the database fixture
    public string ConnectionString => _dbFixture.ConnectionString;
    
    // Parameterless constructor for xUnit collection fixture
    public ApiTestFixture()
    {
        _dbFixture = new TestDatabaseFixture();
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            if (_dbFixture?.ConnectionString != null)
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = _dbFixture.ConnectionString
                });
            }
        });
        
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
        
        builder.ConfigureTestServices(services =>
        {
            // Any test-specific service overrides can go here
        });
    }
    
    public async Task InitializeAsync()
    {
        // Initialize the database fixture first
        await _dbFixture.InitializeAsync();
        
        // Then create the client with the configured web host
        Client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        Console.WriteLine("Test API client created and ready");
    }
    
    // Add 'new' keyword to hide the inherited member
    public new async Task DisposeAsync()
    {
        await _dbFixture.DisposeAsync();
    }
    
    public async Task ResetDatabaseAsync()
    {
        await _dbFixture.ResetDatabaseAsync();
    }
}

// Fix the test collection definition
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<ApiTestFixture>
{
    // This class has no code, and is never created. Its purpose is to be the place
    // to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}