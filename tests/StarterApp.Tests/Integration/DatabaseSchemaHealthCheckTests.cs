using Microsoft.Extensions.Diagnostics.HealthChecks;
using StarterApp.Api.Infrastructure.HealthChecks;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class DatabaseSchemaHealthCheckTests
{
    private static readonly string[] Expected = ["0001_Expand_First.sql", "0002_Expand_Second.sql"];

    private readonly ApiTestFixture _fixture;

    public DatabaseSchemaHealthCheckTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void TheApi_ExpectsEveryScriptTheMigratorShips()
    {
        var shipped = Directory.GetFiles(Path.Combine(TestPaths.RepoRoot, "src", "StarterApp.DbMigrator", "Scripts"), "*.sql", SearchOption.AllDirectories)
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(shipped);
        Assert.Equal(shipped, DatabaseSchemaHealthCheck.EmbeddedScriptNames(typeof(IApiMarker).Assembly));
    }

    [Fact]
    public async Task ADatabaseBehindTheBuild_IsNotReady()
    {
        var result = await CheckAsync(["StarterApp.DbMigrator.Scripts.0001_Expand_First.sql"]);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("0002_Expand_Second.sql", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADatabaseWithNoJournal_IsNotReady()
    {
        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(null)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ADatabaseAtOrAheadOfTheBuild_IsReady(bool ahead)
    {
        string[] applied = ahead
            ? ["StarterApp.DbMigrator.Scripts.0001_Expand_First.sql", "StarterApp.DbMigrator.Scripts.0002_Expand_Second.sql", "StarterApp.DbMigrator.Scripts.0003_Expand_Third.sql"]
            : ["StarterApp.DbMigrator.Scripts.0001_Expand_First.sql", "StarterApp.DbMigrator.Scripts.0002_Expand_Second.sql"];

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(applied)).Status);
    }

    private async Task<HealthCheckResult> CheckAsync(string[]? journal)
    {
        var database = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Database = "schema_" + Guid.NewGuid().ToString("N")[..12] }.ConnectionString;
        await using (var admin = NpgsqlDataSource.Create(_fixture.ConnectionString))
        await using (var create = admin.CreateCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(database).Database}\""))
            await create.ExecuteNonQueryAsync();

        await using var dataSource = NpgsqlDataSource.Create(database);
        if (journal is not null)
        {
            await using (var table = dataSource.CreateCommand("CREATE TABLE schemaversions (schemaversionsid serial PRIMARY KEY, scriptname varchar(255) NOT NULL, applied timestamp NOT NULL DEFAULT now())"))
                await table.ExecuteNonQueryAsync();
            foreach (var name in journal)
            {
                await using var insert = dataSource.CreateCommand("INSERT INTO schemaversions (scriptname) VALUES (@name)");
                insert.Parameters.AddWithValue("name", name);
                await insert.ExecuteNonQueryAsync();
            }
        }

        return await new DatabaseSchemaHealthCheck(dataSource, Expected).CheckHealthAsync(new HealthCheckContext());
    }
}
