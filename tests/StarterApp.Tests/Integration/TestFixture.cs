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

    // The logins the API and the worker sign in as, made by the migrator's role setup as when deployed.
    public const string ApplicationLogin = "starterapp_api";
    public const string WorkerLogin = "starterapp_functions";
    private const string ApplicationPassword = "application-test-password";
    private const string WorkerPassword = "worker-test-password";

    public string ApplicationConnectionString => LoginConnectionString(ApplicationLogin, ApplicationPassword);

    public string WorkerConnectionString => LoginConnectionString(WorkerLogin, WorkerPassword);

    private string LoginConnectionString(string login, string password) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Username = login, Password = password }.ConnectionString;
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

            await StarterApp.DbMigrator.ApplicationRoleSetup.ApplyAsync(ConnectionString,
            [
                new StarterApp.DbMigrator.ApplicationLogin(StarterApp.DbMigrator.ApplicationRoleSetup.ApplicationRole, ApplicationLogin, ApplicationPassword, null),
                new StarterApp.DbMigrator.ApplicationLogin(StarterApp.DbMigrator.ApplicationRoleSetup.WorkerRole, WorkerLogin, WorkerPassword, null),
            ]);

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
            // The migrator's own path: its per-script transactions and its no-transaction scripts, as when deployed.
            return StarterApp.DbMigrator.DatabaseMigrationEngine.MigrateDatabase(connectionString, Assembly.Load("StarterApp.DbMigrator"));
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
            // Replica mode skips the triggers that make a table append-only; only a superuser may set it.
            await using (var replica = new NpgsqlCommand("SET session_replication_role = replica", connection))
                await replica.ExecuteNonQueryAsync();
            try
            {
                await _respawner.ResetAsync(connection);
            }
            finally
            {
                await using var restore = new NpgsqlCommand("RESET session_replication_role", connection);
                await restore.ExecuteNonQueryAsync();
            }

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
    private int _disposed;
    public InMemoryPayloadArchiveStore PayloadArchiveStore { get; } = new();
    public HttpClient Client { get; private set; } = null!;

    // The administrator's: setup and checks that see every tenant.
    public string ConnectionString => _dbFixture.ConnectionString;

    public string ApplicationConnectionString => _dbFixture.ApplicationConnectionString;

    public string WorkerConnectionString => _dbFixture.WorkerConnectionString;

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
            configuration.AddInMemoryCollection(TestJwtIdentity.Configuration);
            // Every test calls as one subject, so the per-subject limit would queue the suite a window at a time.
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:PermitLimit"] = "1000000" });
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
            TestJwtIdentity.ConfigureTestJwtValidation(services);
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
            Environment.SetEnvironmentVariable("ConnectionStrings__database", _dbFixture.ApplicationConnectionString);

            // Then create the client with the configured web host
            ClientOptions.AllowAutoRedirect = false;
            Client = CreateDefaultClient(new JwtSigningHandler());
            Log.Information("Test API client created and ready");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error initializing ApiTestFixture");
            throw;
        }
    }
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    // xUnit disposes a collection fixture twice (IAsyncLifetime, then IAsyncDisposable); a second pass re-stops hosts that are already gone.
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        Environment.SetEnvironmentVariable("ConnectionStrings__database", null);
        try
        {
            Client?.Dispose();
            await base.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error disposing the API hosts");
        }

        await _dbFixture.DisposeAsync();
        GC.SuppressFinalize(this);
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
