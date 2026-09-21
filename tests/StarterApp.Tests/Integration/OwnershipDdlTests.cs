namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class OwnershipDdlTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;

    public OwnershipDdlTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public async Task DisposeAsync() => await Task.CompletedTask;

    // Migration 0004 dropped the 'legacy-owner'/'legacy-tenant' column DEFAULTs: raw SQL
    // that forgets the owner columns must fail with not_null_violation (23502) instead of
    // silently creating rows owned by a placeholder identity.
    [Fact]
    public async Task InsertWithoutOwnerColumns_FailsWithNotNullViolation()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "INSERT INTO customers (name, email) VALUES ('No Owner', 'no-owner@example.test')", connection);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("23502", exception.SqlState);
    }
}
