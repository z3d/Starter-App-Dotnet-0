using DbUp.Helpers;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class PerfSeedScriptTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerfSeedScriptTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Resetting database for perf-seed test");
        await _fixture.ResetDatabaseAsync();
    }

    public async Task DisposeAsync() => await Task.CompletedTask;

    [Fact]
    public async Task PerfSeedScript_SeedsBulkOwnerScopedData_AndIsIdempotent()
    {
        var script = await System.IO.File.ReadAllTextAsync(SeedScriptPath());

        ExecuteScript(script);
        var firstRun = await CountSeededRowsAsync();

        Assert.True(firstRun.Customers >= 20000, $"Expected >= 20000 seeded customers, got {firstRun.Customers}");
        Assert.True(firstRun.Products >= 20000, $"Expected >= 20000 seeded products, got {firstRun.Products}");
        Assert.True(firstRun.Orders >= 20000, $"Expected >= 20000 seeded orders, got {firstRun.Orders}");
        Assert.True(firstRun.OrderItems >= firstRun.Orders, $"Expected >= 1 item per seeded order, got {firstRun.OrderItems} items for {firstRun.Orders} orders");
        Assert.Equal(0, firstRun.ForeignOwnerRows);

        ExecuteScript(script);
        var secondRun = await CountSeededRowsAsync();

        Assert.Equal(firstRun.Customers, secondRun.Customers);
        Assert.Equal(firstRun.Products, secondRun.Products);
        Assert.Equal(firstRun.Orders, secondRun.Orders);
        Assert.Equal(firstRun.OrderItems, secondRun.OrderItems);
    }

    private static string SeedScriptPath()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "StarterApp.slnx")))
                return System.IO.Path.Combine(directory.FullName, "tests", "k6", "seed", "perf-seed.sql");
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }

    private void ExecuteScript(string script)
    {
        // DbUp is the established in-repo pattern for executing dynamic SQL in tests
        // (see DbUpApiTests). NullJournal keeps the script unjournaled so the
        // idempotency re-run actually executes instead of being skipped.
        var upgradeEngine = DeployChanges.To
            .PostgresqlDatabase(_fixture.ConnectionString)
            .WithScript("PerfSeed", script)
            .JournalTo(new NullJournal())
            .WithExecutionTimeout(TimeSpan.FromMinutes(2))
            .Build();

        var result = upgradeEngine.PerformUpgrade();
        Assert.True(result.Successful, $"perf-seed.sql failed against the current schema: {result.Error}");
    }

    private async Task<(long Customers, long Products, long Orders, long OrderItems, long ForeignOwnerRows)> CountSeededRowsAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var customers = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM customers WHERE owner_subject = 'k6-user' AND tenant_id = 'k6-tenant' AND email LIKE 'perf-seed-%'", connection));
        var products = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM products WHERE owner_subject = 'k6-user' AND tenant_id = 'k6-tenant' AND name LIKE 'Perf Seed Product %'", connection));
        var orders = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM orders WHERE owner_subject = 'k6-user' AND tenant_id = 'k6-tenant'", connection));
        var orderItems = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM order_items oi JOIN orders o ON o.id = oi.order_id WHERE o.owner_subject = 'k6-user'", connection));
        var foreignOwners = await ScalarAsync(new NpgsqlCommand(
            "SELECT (SELECT count(*) FROM customers WHERE email LIKE 'perf-seed-%' AND (owner_subject <> 'k6-user' OR tenant_id <> 'k6-tenant'))" +
            " + (SELECT count(*) FROM products WHERE name LIKE 'Perf Seed Product %' AND (owner_subject <> 'k6-user' OR tenant_id <> 'k6-tenant'))", connection));

        return (customers, products, orders, orderItems, foreignOwners);
    }

    private static async Task<long> ScalarAsync(NpgsqlCommand command)
    {
        await using (command)
        {
            command.CommandTimeout = 120;
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
