using DbUp.Helpers;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class DastSeedScriptTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DastSeedScriptTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Resetting database for dast-seed test");
        await _fixture.ResetDatabaseAsync();
    }

    public async Task DisposeAsync() => await Task.CompletedTask;

    [Fact]
    public async Task DastSeedScript_SeedsBothOwners_AndIsIdempotent()
    {
        var script = await System.IO.File.ReadAllTextAsync(SeedScriptPath());

        ExecuteScript(script);
        var firstRun = await CountSeededRowsAsync();

        // Owner 1 (the scanned identity) — modest, non-empty volume so by-id/list probes hit real rows.
        Assert.True(firstRun.Owner1Customers >= 50, $"Expected >= 50 owner-1 customers, got {firstRun.Owner1Customers}");
        Assert.True(firstRun.Owner1Products >= 50, $"Expected >= 50 owner-1 products, got {firstRun.Owner1Products}");
        Assert.True(firstRun.Owner1Orders >= 50, $"Expected >= 50 owner-1 orders, got {firstRun.Owner1Orders}");
        Assert.True(firstRun.Owner1OrderItems >= firstRun.Owner1Orders, $"Expected >= 1 item per owner-1 order, got {firstRun.Owner1OrderItems} for {firstRun.Owner1Orders} orders");

        // Owner 2 (the IDOR target) — the fixed-id fixtures the cross-owner probe references.
        Assert.True(firstRun.CrossOwnerCustomerExists, "Fixed-id owner-2 customer (900001) must exist for the IDOR probe.");
        Assert.True(firstRun.CrossOwnerProductExists, "Fixed-id owner-2 product (900001) must exist for the IDOR probe.");
        Assert.True(firstRun.CrossOwnerOrderExists, "Fixed-guid owner-2 order must exist for the IDOR probe.");

        ExecuteScript(script);
        var secondRun = await CountSeededRowsAsync();

        Assert.Equal(firstRun.Owner1Customers, secondRun.Owner1Customers);
        Assert.Equal(firstRun.Owner1Products, secondRun.Owner1Products);
        Assert.Equal(firstRun.Owner1Orders, secondRun.Owner1Orders);
        Assert.Equal(firstRun.Owner1OrderItems, secondRun.Owner1OrderItems);
        Assert.Equal(firstRun.Owner2Customers, secondRun.Owner2Customers);
        Assert.Equal(firstRun.Owner2Products, secondRun.Owner2Products);
        Assert.Equal(firstRun.Owner2Orders, secondRun.Owner2Orders);
    }

    private static string SeedScriptPath()
    {
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "StarterApp.slnx")))
                return System.IO.Path.Combine(directory.FullName, "dast", "seed", "dast-seed.sql");
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repo root (StarterApp.slnx) from the test base directory.");
    }

    private void ExecuteScript(string script)
    {
        // DbUp is the established in-repo pattern for executing dynamic SQL in tests
        // (see DbUpApiTests / PerfSeedScriptTests). NullJournal keeps the script
        // unjournaled so the idempotency re-run actually executes instead of being skipped.
        var upgradeEngine = DeployChanges.To
            .PostgresqlDatabase(_fixture.ConnectionString)
            .WithScript("DastSeed", script)
            .JournalTo(new NullJournal())
            .WithExecutionTimeout(TimeSpan.FromMinutes(2))
            .Build();

        var result = upgradeEngine.PerformUpgrade();
        Assert.True(result.Successful, $"dast-seed.sql failed against the current schema: {result.Error}");
    }

    private async Task<(
        long Owner1Customers, long Owner1Products, long Owner1Orders, long Owner1OrderItems,
        long Owner2Customers, long Owner2Products, long Owner2Orders,
        bool CrossOwnerCustomerExists, bool CrossOwnerProductExists, bool CrossOwnerOrderExists)> CountSeededRowsAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var owner1Customers = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM customers WHERE owner_subject = 'dast-user-01' AND tenant_id = 'dast-tenant-01' AND email LIKE 'dast-seed-%'", connection));
        var owner1Products = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM products WHERE owner_subject = 'dast-user-01' AND tenant_id = 'dast-tenant-01' AND name LIKE 'DAST Seed Product %'", connection));
        var owner1Orders = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM orders WHERE owner_subject = 'dast-user-01' AND tenant_id = 'dast-tenant-01'", connection));
        var owner1OrderItems = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM order_items oi JOIN orders o ON o.id = oi.order_id WHERE o.owner_subject = 'dast-user-01'", connection));

        var owner2Customers = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM customers WHERE owner_subject = 'dast-user-02' AND tenant_id = 'dast-tenant-02'", connection));
        var owner2Products = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM products WHERE owner_subject = 'dast-user-02' AND tenant_id = 'dast-tenant-02'", connection));
        var owner2Orders = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM orders WHERE owner_subject = 'dast-user-02' AND tenant_id = 'dast-tenant-02'", connection));

        var crossOwnerCustomer = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM customers WHERE id = 900001 AND owner_subject = 'dast-user-02'", connection));
        var crossOwnerProduct = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM products WHERE id = 900001 AND owner_subject = 'dast-user-02'", connection));
        var crossOwnerOrder = await ScalarAsync(new NpgsqlCommand(
            "SELECT count(*) FROM orders WHERE id = '00000000-0000-0000-0000-0000dad70002' AND owner_subject = 'dast-user-02'", connection));

        return (
            owner1Customers, owner1Products, owner1Orders, owner1OrderItems,
            owner2Customers, owner2Products, owner2Orders,
            crossOwnerCustomer > 0, crossOwnerProduct > 0, crossOwnerOrder > 0);
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
