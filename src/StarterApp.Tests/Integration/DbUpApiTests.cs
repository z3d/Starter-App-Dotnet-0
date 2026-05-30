namespace StarterApp.Tests.Integration;

[Collection("Integration Tests")]
public class DbUpApiTests : IAsyncLifetime
{
    private readonly ApiTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DbUpApiTests(ApiTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        // Reset database before each test
        try
        {
            _output.WriteLine("Resetting database for DbUp test");
            await _fixture.ResetDatabaseAsync();
            _output.WriteLine("Database reset complete for DbUp test");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Error during database reset for DbUp test: {ex.GetType().Name}");
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        // No additional cleanup needed for each test
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ShouldUseDbUpToInsertData_ThenQueryViaApi()
    {
        // Arrange - Set up data directly with SQL via DbUp
        var productId = 1000;
        var productName = "DbUp Inserted Product";
        var productDescription = "This product was inserted using DbUp directly";
        var price = 99.99m;
        var stock = 42;

        // Use DbUp to directly insert data - directly inserting values
        var insertScript = $@"
            INSERT INTO products (id, name, description, price_amount, price_currency, stock, last_updated, owner_subject, tenant_id)
            VALUES ({productId}, '{productName.Replace("'", "''")}', '{productDescription.Replace("'", "''")}', {price.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'USD', {stock}, now(), '{TestGatewayIdentity.DefaultSubject}', '{TestGatewayIdentity.DefaultTenantId}');
        ";

        // Configure DbUp to use a custom journal table name with JournalTo
        var upgradeEngine = DeployChanges.To
            .PostgresqlDatabase(_fixture.ConnectionString)
            .WithScript("InsertTestProduct", insertScript)
            .JournalToPostgresqlTable("public", "custom_schema_versions")
            .WithTransaction()
            .Build();

        var result = upgradeEngine.PerformUpgrade();
        Assert.True(result.Successful, $"DbUp script execution failed: {result.Error}");

        // Act - Query the API to get the product
        var response = await _fixture.Client.GetAsync($"/api/v1/products/{productId}");

        // Assert
        response.EnsureSuccessStatusCode();
        var retrievedProduct = await response.Content.ReadFromJsonAsync<ProductReadModel>();

        Assert.NotNull(retrievedProduct);
        Assert.Equal(productId, retrievedProduct.Id);
        Assert.Equal(productName, retrievedProduct.Name);
        Assert.Equal(productDescription, retrievedProduct.Description);
        Assert.Equal(price, retrievedProduct.PriceAmount);
        Assert.Equal(stock, retrievedProduct.Stock);
    }

    [Fact]
    public async Task ShouldCreateProductViaApi_ThenVerifyWithPostgresQuery()
    {
        // Arrange
        var newProduct = new CreateProductCommand
        {
            Name = "API Created Product",
            Description = "This product was created via the API",
            Price = 49.95m,
            Currency = "USD",
            Stock = 25
        };

        // Act - Create product via API
        var response = await _fixture.Client.PostAsJsonAsync("/api/v1/products", newProduct);
        response.EnsureSuccessStatusCode();
        var createdProduct = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(createdProduct);

        // Assert - Verify directly in database using SQL with parameterized query
        using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        // Use parameterized query instead of string interpolation
        using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM products WHERE id = @Id AND name = @Name",
            connection);

        command.Parameters.AddWithValue("@Id", createdProduct.Id);
        command.Parameters.AddWithValue("@Name", newProduct.Name);

        var result = await command.ExecuteScalarAsync();
        var count = result != null ? Convert.ToInt32(result) : 0;

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ShouldRunCustomDbUpScript_ThenCleanUp()
    {
        // Step 1: Create test table using DbUp
        var createTableScript = @"
            CREATE TABLE test_table (
                id integer CONSTRAINT pk_test_table PRIMARY KEY,
                name varchar(100) NOT NULL,
                created_at timestamptz NOT NULL CONSTRAINT df_test_table_created_at DEFAULT now()
            )
        ";

        var insertDataScript = @"
            INSERT INTO test_table (id, name) VALUES (1, 'Test Item 1');
            INSERT INTO test_table (id, name) VALUES (2, 'Test Item 2');
            INSERT INTO test_table (id, name) VALUES (3, 'Test Item 3');
        ";

        // Run script with DbUp and use a custom journal table to avoid conflicts
        var upgradeEngine = DeployChanges.To
            .PostgresqlDatabase(_fixture.ConnectionString)
            .WithScript("CreateTestTable", createTableScript)
            .WithScript("InsertTestData", insertDataScript)
            .JournalToPostgresqlTable("public", "custom_schema_versions")
            .WithTransaction()
            .Build();

        var dbUpResult = upgradeEngine.PerformUpgrade();
        Assert.True(dbUpResult.Successful, $"DbUp script execution failed: {dbUpResult.Error}");

        // Step 2: Verify table and data exists using parameterized query
        using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        using var command = new NpgsqlCommand("SELECT COUNT(*) FROM test_table", connection);
        var queryResult = await command.ExecuteScalarAsync();
        var count = queryResult != null ? Convert.ToInt32(queryResult) : 0;

        Assert.Equal(3, count);

        // Step 3: Clean up (drop the test table)
        var dropTableScript = "DROP TABLE test_table";

        var cleanupEngine = DeployChanges.To
            .PostgresqlDatabase(_fixture.ConnectionString)
            .WithScript("DropTestTable", dropTableScript)
            .JournalToPostgresqlTable("public", "custom_schema_versions")
            .WithTransaction()
            .Build();

        var cleanupResult = cleanupEngine.PerformUpgrade();
        Assert.True(cleanupResult.Successful, $"DbUp cleanup script execution failed: {cleanupResult.Error}");

        // Step 4: Verify table was dropped using parameterized query
        using var connection2 = new NpgsqlConnection(_fixture.ConnectionString);
        await connection2.OpenAsync();

        using var command2 = new NpgsqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @TableName",
            connection2);

        command2.Parameters.AddWithValue("@TableName", "test_table");

        var dropQueryResult = await command2.ExecuteScalarAsync();
        var tableExists = dropQueryResult != null ? Convert.ToInt32(dropQueryResult) : 0;

        Assert.Equal(0, tableExists);
    }
}
