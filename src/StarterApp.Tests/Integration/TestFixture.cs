using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Respawn;
using Respawn.Graph;
using StarterApp.ServiceDefaults.Payloads;
using Testcontainers.PostgreSql;

namespace StarterApp.Tests.Integration;

public class TestDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgresContainer;
    public string ConnectionString { get; private set; } = null!;
    private Respawner _respawner = null!;
    private const string DbName = "starterapp_tests";
    private const string DbUsername = "postgres";
    private const string DbPassword = "postgres";

    public TestDatabaseFixture()
    {
        _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(DbName)
            .WithUsername(DbUsername)
            .WithPassword(DbPassword)
            .Build();
    }

    public async Task InitializeAsync()
    {
        try
        {
            Console.WriteLine("Starting PostgreSQL container...");
            await _postgresContainer.StartAsync();
            ConnectionString = _postgresContainer.GetConnectionString();
            Console.WriteLine($"Using connection string: {ConnectionString}");

            // Run DbUp migrations on the test database
            Console.WriteLine("Applying database migrations using DbUp...");
            if (!RunDbUpMigrations(ConnectionString))
            {
                throw new InvalidOperationException("Failed to apply database migrations");
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

    private static bool RunDbUpMigrations(string connectionString)
    {
        try
        {
            // Use DbUp to run migrations from the DbMigrator project
            var migratorAssembly = Assembly.Load("StarterApp.DbMigrator");

            var upgrader = DeployChanges.To
                .PostgresqlDatabase(connectionString)
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
            await _postgresContainer.DisposeAsync();
            Console.WriteLine("PostgreSQL container disposed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error disposing PostgreSQL container: {ex}");
        }
    }

    private async Task InitializeRespawner()
    {
        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = new[] { "public" },
                TablesToIgnore = new Table[] { new Table("schemaversions") }
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
            using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();

            // DbUp manages its schema journal, and Respawn leaves it alone between tests.
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
    public InMemoryPayloadArchiveStore PayloadArchiveStore { get; } = new();
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
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // The connection string is set as an environment variable in InitializeAsync
        // before this method is called, so Program.cs should find it successfully

        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(TestGatewayIdentity.Configuration);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(_logger);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPayloadArchiveStore>();
            services.AddSingleton<IPayloadArchiveStore>(PayloadArchiveStore);
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
            Environment.SetEnvironmentVariable("ConnectionStrings__database", _dbFixture.ConnectionString);
            Log.Information($"Set connection string environment variable: {_dbFixture.ConnectionString}");

            // Then create the client with the configured web host
            ClientOptions.AllowAutoRedirect = false;
            Client = CreateDefaultClient(new GatewayIdentitySigningHandler());
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
            Environment.SetEnvironmentVariable("ConnectionStrings__database", null);

            Client?.Dispose();
            base.Dispose();
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
            PayloadArchiveStore.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error resetting database in ApiTestFixture");
            throw;
        }
    }

    public HttpClient CreateUnauthenticatedClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }
}

// Fix the test collection definition
[CollectionDefinition("Integration Tests")]
public class IntegrationTestCollection : ICollectionFixture<ApiTestFixture>
{
    // This class has no code, and is never created. Its purpose is to be the place
    // to apply [CollectionDefinition] and all the ICollectionFixture<> interfaces.
}
