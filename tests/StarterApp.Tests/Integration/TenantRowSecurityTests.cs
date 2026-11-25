using System.Globalization;
using Dapper;
using StarterApp.Functions;
using static StarterApp.Tests.Conventions.TenantRowSecurityRules;

namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class TenantRowSecurityTests : IAsyncLifetime
{
    private const string TenantA = "rls-tenant-a";
    private const string TenantB = "rls-tenant-b";

    private const string TablesSql = """
        SELECT n.nspname || '.' || c.relname,
               EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped),
               c.relrowsecurity, c.relforcerowsecurity
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind IN ('r', 'p')
          AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
        """;

    private const string PoliciesSql = """
        SELECT schemaname || '.' || tablename, policyname, permissive = 'PERMISSIVE', roles::text[], cmd, qual, with_check
        FROM pg_policies
        """;

    private readonly ApiTestFixture _fixture;

    public TenantRowSecurityTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EveryTableWithATenantId_IsForcedUnderItsTenantPolicy_AndEveryOtherTableHasAStatedReason()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        var (tables, policies) = await ReadCatalogAsync(connection, null);

        Assert.Contains(tables, table => table.HasTenantId);
        var failures = Violations(tables, policies, PlatformTables, CrossTenantPolicies);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task EveryListedPlatformTableAndCrossTenantPolicy_StillExistsAsListed()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        var (tables, policies) = await ReadCatalogAsync(connection, null);

        var stale = StaleEntries(tables, policies, PlatformTables, CrossTenantPolicies);
        Assert.True(stale.Count == 0, string.Join("\n", stale));
    }

    [Fact]
    public async Task TheCatalogRule_SeesATableAMigrationForgotToProtect()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "CREATE TABLE made_up_invoices (id int NOT NULL, tenant_id varchar(100) NOT NULL); CREATE TABLE made_up_rates (id int NOT NULL)", transaction);

        var (tables, policies) = await ReadCatalogAsync(connection, transaction);
        var failures = Violations(tables, policies, PlatformTables, CrossTenantPolicies);

        Assert.Contains(failures, failure => failure.StartsWith("public.made_up_invoices has a tenant_id but row-level security is not enabled", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.StartsWith("public.made_up_rates has no tenant_id", StringComparison.Ordinal));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task EveryTable_IsUsableByBothApplicationRoles_AndTheJournalOnlyReadByTheApi()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);

        var failures = await UnusableTablesAsync(connection, null);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task TheGrantRule_SeesATableCreatedOutsideTheMigrator()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, "CREATE TABLE made_up_ungranted (id int NOT NULL)", transaction);

        var failures = await UnusableTablesAsync(connection, transaction);

        Assert.Contains(failures, failure => failure.Contains("public.made_up_ungranted", StringComparison.Ordinal));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TheApplicationRoles_CannotBypassRowSecurity_AndOwnNothing()
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        await using var command = new NpgsqlCommand("""
            SELECT r.rolname, r.rolsuper, r.rolbypassrls, r.rolcanlogin, r.rolcreaterole, r.rolcreatedb,
                   (SELECT count(*) FROM pg_class c WHERE c.relowner = r.oid)
            FROM pg_roles r WHERE r.rolname = ANY(@roles) ORDER BY r.rolname
            """, connection);
        command.Parameters.AddWithValue("roles", ApplicationRoleSetup.Roles.ToArray());
        var rows = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add($"{reader.GetString(0)} super={reader.GetBoolean(1)} bypass={reader.GetBoolean(2)} login={reader.GetBoolean(3)} createrole={reader.GetBoolean(4)} createdb={reader.GetBoolean(5)} owns={reader.GetInt64(6)}");
        }

        Assert.Equal(
            ApplicationRoleSetup.Roles.Order(StringComparer.Ordinal).Select(role => $"{role} super=False bypass=False login=False createrole=False createdb=False owns=0"),
            rows);
    }

    [Theory]
    [InlineData(TestDatabaseFixture.ApplicationLogin)]
    [InlineData(TestDatabaseFixture.WorkerLogin)]
    public async Task TheLogins_CannotSwitchRowSecurityOff(string login)
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);

        Assert.Empty(await StarterApp.ServiceDefaults.DatabaseLoginGuard.FindViolationsAsync(connection, login, CancellationToken.None));
    }

    [Theory]
    [InlineData("CREATE ROLE made_up_login LOGIN BYPASSRLS", "may bypass row-level security")]
    [InlineData("CREATE ROLE made_up_login LOGIN CREATEROLE", "may create and grant roles")]
    [InlineData("CREATE ROLE made_up_login LOGIN; GRANT postgres TO made_up_login", "may act as postgres")]
    [InlineData("CREATE ROLE made_up_login LOGIN SUPERUSER", "is a superuser")]
    public async Task TheLoginGuard_RefusesALoginThatCouldSwitchRowSecurityOff(string sql, string expected)
    {
        await using var connection = await OpenAsync(_fixture.ConnectionString);
        await using var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, sql, transaction);

        var violations = await StarterApp.ServiceDefaults.DatabaseLoginGuard.FindViolationsAsync(connection, "made_up_login", CancellationToken.None);

        Assert.Contains(violations, violation => violation.Contains(expected, StringComparison.Ordinal));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TheLoginGuard_StopsAProcessThatSignsInAsTheAdministrator()
    {
        await using var dataSource = NpgsqlDataSource.Create(_fixture.ConnectionString);
        var guard = new StarterApp.ServiceDefaults.DatabaseLoginGuardService(dataSource);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.StartAsync(CancellationToken.None));
        Assert.Contains("is a superuser", refusal.Message, StringComparison.Ordinal);

        await using var applicationSource = NpgsqlDataSource.Create(_fixture.ApplicationConnectionString);
        await new StarterApp.ServiceDefaults.DatabaseLoginGuardService(applicationSource).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheLoginGuard_SeesTheLoginBehindARoleSetAtConnect()
    {
        var masked = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString) { Options = "-c role=starterapp_app" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(masked);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => new StarterApp.ServiceDefaults.DatabaseLoginGuardService(dataSource).StartAsync(CancellationToken.None));
        Assert.Contains("is a superuser", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApplicationLogin_BoundToOneTenant_CannotReadChangeOrPlantAnothersRows()
    {
        var (customerA, _) = await SeedTwoTenantsAsync();
        await using var connection = await OpenAsync(_fixture.ApplicationConnectionString);
        await BindAsync(connection, TenantB);

        Assert.Equal(1L, await ScalarAsync(connection, "SELECT count(*) FROM customers"));
        Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM customers WHERE id = {customerA}"));
        Assert.Equal(0, await ExecuteAsync(connection, $"UPDATE customers SET name = 'taken' WHERE id = {customerA}"));
        Assert.Equal(0, await ExecuteAsync(connection, $"DELETE FROM customers WHERE id = {customerA}"));

        var planted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
            $"INSERT INTO customers (name, email, owner_subject, tenant_id) VALUES ('planted', 'planted@example.test', 'someone', '{TenantA}')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, planted.SqlState);

        var moved = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, $"UPDATE customers SET tenant_id = '{TenantA}'"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, moved.SqlState);

        await using var admin = await OpenAsync(_fixture.ConnectionString);
        Assert.Equal("A", await ScalarAsync(admin, $"SELECT name FROM customers WHERE id = {customerA}"));
    }

    [Fact]
    public async Task TheApplicationLogin_WithNoTenantBound_SeesNoTenantsRows_InAnyTenantTable()
    {
        await SeedTwoTenantsAsync();
        await using var admin = await OpenAsync(_fixture.ConnectionString);
        var (tables, _) = await ReadCatalogAsync(admin, null);

        await using var connection = await OpenAsync(_fixture.ApplicationConnectionString);
        foreach (var table in tables.Where(table => table.HasTenantId))
            Assert.Equal(0L, await ScalarAsync(connection, $"SELECT count(*) FROM {table.Name}"));

        await BindAsync(connection, string.Empty);
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT count(*) FROM customers"));
    }

    [Theory]
    [InlineData("SET ROLE postgres")]
    [InlineData("ALTER TABLE customers NO FORCE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE customers DISABLE ROW LEVEL SECURITY")]
    [InlineData("DROP POLICY tenant_isolation_customers ON customers")]
    [InlineData("SET row_security = off; SELECT count(*) FROM customers")]
    public async Task TheApplicationLogin_CannotSwitchRowSecurityOff(string sql)
    {
        await SeedTwoTenantsAsync();
        await using var connection = await OpenAsync(_fixture.ApplicationConnectionString);

        var refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    [Fact]
    public async Task APooledConnection_DoesNotCarryOneRequestsTenantToTheNext()
    {
        var (customerA, _) = await SeedTwoTenantsAsync();
        var pooledOne = new NpgsqlConnectionStringBuilder(_fixture.ApplicationConnectionString) { MaxPoolSize = 1 }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(pooledOne);

        int backend;
        await using (var tenantConnection = StarterApp.Application.Infrastructure.Tenancy.TenantDbConnection.Create(dataSource, User(TenantA)))
        {
            Assert.Equal(1, await tenantConnection.ExecuteScalarAsync<int>($"SELECT count(*) FROM customers WHERE id = {customerA}"));
            backend = await tenantConnection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()");
        }

        await using var next = await dataSource.OpenConnectionAsync();
        Assert.Equal(backend, await next.ExecuteScalarAsync<int>("SELECT pg_backend_pid()"));
        Assert.True(string.IsNullOrEmpty(await next.ExecuteScalarAsync<string?>("SELECT current_setting('app.tenant_id', true)")));
        Assert.Equal(0, await next.ExecuteScalarAsync<int>("SELECT count(*) FROM customers"));
    }

    [Fact]
    public async Task TheApiReadAndWritePaths_AreBoundToTheCallersTenant()
    {
        var (customerA, customerB) = await SeedTwoTenantsAsync();
        using var scope = _fixture.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CurrentUserAccessor>().Set(User(TenantB));

        var reads = scope.ServiceProvider.GetRequiredService<System.Data.IDbConnection>();
        Assert.Equal([customerB], (await reads.QueryAsync<int>("SELECT id FROM customers")).ToList());

        var writes = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null(await writes.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(customer => customer.Id == customerA));
        writes.Customers.Add(new Customer("Planted", Email.Create("planted@example.test"), "someone", TenantA));
        var planted = await Assert.ThrowsAsync<DbUpdateException>(() => writes.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, Assert.IsType<PostgresException>(planted.InnerException).SqlState);
    }

    [Fact]
    public async Task TheWorkerLogin_PurgesEveryTenantsExpiredKeys_AndReadsNoTenantsOtherRows()
    {
        await SeedTwoTenantsAsync();
        await using (var admin = await OpenAsync(_fixture.ConnectionString))
        {
            await ExecuteAsync(admin, $"""
                INSERT INTO idempotency_records (tenant_id, owner_subject, operation, idempotency_key, request_hash, resource_id, created_on_utc)
                VALUES ('{TenantA}', 's', 'op', 'k1', 'h', 'r', now() - interval '30 days'),
                       ('{TenantB}', 's', 'op', 'k2', 'h', 'r', now() - interval '30 days'),
                       ('{TenantB}', 's', 'op', 'k3', 'h', 'r', now())
                """);
        }

        await using var dataSource = NpgsqlDataSource.Create(_fixture.WorkerConnectionString);
        var purged = await new IdempotencyRecordPurge(dataSource).DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-1), TimeSpan.FromMinutes(1), TimeProvider.System, CancellationToken.None);
        Assert.Equal(2, purged.Deleted);

        await using var worker = await dataSource.OpenConnectionAsync();
        Assert.Equal(0L, await ScalarAsync(worker, "SELECT count(*) FROM customers"));
        var planted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(worker,
            $"INSERT INTO idempotency_records (tenant_id, owner_subject, operation, idempotency_key, request_hash, resource_id) VALUES ('{TenantA}', 's', 'op', 'k4', 'h', 'r')"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, planted.SqlState);
    }

    [Fact]
    public async Task TheInbox_StartsWithNoTenant_AndAHandlerSeesOnlyTheTenantItBinds()
    {
        await SeedTwoTenantsAsync();
        await using var dataSource = NpgsqlDataSource.Create(_fixture.WorkerConnectionString);
        var inbox = new PostgresMessageInbox(dataSource, retentionDays: 7, TimeProvider.System, NullLogger<PostgresMessageInbox>.Instance);
        long unbound = -1, bound = -1;

        await inbox.ProcessOnceAsync("tenant-probe", Guid.NewGuid().ToString(), async (transaction, ct) =>
        {
            unbound = (long)(await ScalarAsync(transaction!.Connection, "SELECT count(*) FROM customers", transaction.Transaction))!;
            await transaction.BindTenantAsync(TenantA, ct);
            bound = (long)(await ScalarAsync(transaction.Connection, "SELECT count(*) FROM customers", transaction.Transaction))!;
        }, CancellationToken.None);

        Assert.Equal(0L, unbound);
        Assert.Equal(1L, bound);
    }

    private static CurrentUser User(string tenantId) => new("rls-subject", AuthenticatedPrincipalType.User, tenantId, []);

    private async Task<(int CustomerA, int CustomerB)> SeedTwoTenantsAsync()
    {
        await using var admin = await OpenAsync(_fixture.ConnectionString);
        var a = Convert.ToInt32(await ScalarAsync(admin, $"INSERT INTO customers (name, email, owner_subject, tenant_id) VALUES ('A', 'a@example.test', 'rls-subject', '{TenantA}') RETURNING id"), CultureInfo.InvariantCulture);
        var b = Convert.ToInt32(await ScalarAsync(admin, $"INSERT INTO customers (name, email, owner_subject, tenant_id) VALUES ('B', 'b@example.test', 'rls-subject', '{TenantB}') RETURNING id"), CultureInfo.InvariantCulture);
        return (a, b);
    }

    private static async Task<(IReadOnlyList<Table> Tables, IReadOnlyList<Policy> Policies)> ReadCatalogAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        var tables = new List<Table>();
        await using (var command = new NpgsqlCommand(TablesSql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                tables.Add(new Table(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3)));
        }

        var policies = new List<Policy>();
        await using (var command = new NpgsqlCommand(PoliciesSql, connection, transaction))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                policies.Add(new Policy(reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), await reader.GetFieldValueAsync<string[]>(3), reader.GetString(4),
                    await reader.IsDBNullAsync(5) ? null : reader.GetString(5), await reader.IsDBNullAsync(6) ? null : reader.GetString(6)));
        }

        return (tables, policies);
    }

    private static async Task<IReadOnlyList<string>> UnusableTablesAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT n.nspname || '.' || c.relname, role.name,
                   has_table_privilege(role.name, c.oid, 'SELECT'), has_table_privilege(role.name, c.oid, 'INSERT'),
                   has_table_privilege(role.name, c.oid, 'UPDATE'), has_table_privilege(role.name, c.oid, 'DELETE')
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN unnest(@roles) AS role(name)
            WHERE c.relkind IN ('r', 'p')
              AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg\_%'
            ORDER BY 1, 2
            """, connection, transaction);
        command.Parameters.AddWithValue("roles", ApplicationRoleSetup.Roles.ToArray());
        var failures = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var (table, role) = (reader.GetString(0), reader.GetString(1));
            var held = $"{reader.GetBoolean(2)}/{reader.GetBoolean(3)}/{reader.GetBoolean(4)}/{reader.GetBoolean(5)}";
            var expected = table != "public.schemaversions" ? "True/True/True/True"
                : role == ApplicationRoleSetup.ApplicationRole ? "True/False/False/False" : "False/False/False/False";
            if (held != expected)
                failures.Add($"{role} holds SELECT/INSERT/UPDATE/DELETE {held} on {table}, expected {expected}: the migrator's role setup grants every table on each run.");
        }

        return failures;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task BindAsync(NpgsqlConnection connection, string tenantId)
    {
        await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, false)", connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null) =>
        connection.ExecuteAsync(sql, transaction: transaction);

    private static Task<object?> ScalarAsync(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null) =>
        connection.ExecuteScalarAsync(sql, transaction: transaction);
}
