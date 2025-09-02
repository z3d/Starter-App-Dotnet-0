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
            SET IDENTITY_INSERT Products ON;
            
            INSERT INTO Products (Id, Name, Description, PriceAmount, PriceCurrency, Stock, LastUpdated)
            VALUES ({productId}, '{productName.Replace("'", "''")}', '{productDescription.Replace("'", "''")}', {price.ToString().Replace(',', '.')}, 'USD', {stock}, GETUTCDATE());
            
            SET IDENTITY_INSERT Products OFF;
        ";

        // Configure DbUp to use a custom journal table name with JournalTo
        var upgradeEngine = DeployChanges.To
            .SqlDatabase(_fixture.ConnectionString)
            .WithScript("InsertTestProduct", insertScript)
            .JournalToSqlTable("dbo", "__CustomSchemaVersions")  // Use a different journal table for these tests
            .WithTransaction()
            .Build();

        var result = upgradeEngine.PerformUpgrade();
        Assert.True(result.Successful, $"DbUp script execution failed: {result.Error}");

        // Act - Query the API to get the product
        var response = await _fixture.Client.GetAsync($"/api/products/{productId}");

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
    public async Task ShouldCreateProductViaApi_ThenVerifyWithSqlQuery()
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
        var response = await _fixture.Client.PostAsJsonAsync("/api/products", newProduct);
        response.EnsureSuccessStatusCode();
        var createdProduct = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(createdProduct);

        // Assert - Verify directly in database using SQL with parameterized query
        using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        // Use parameterized query instead of string interpolation
        using var command = new SqlCommand(
            "SELECT COUNT(*) FROM Products WHERE Id = @Id AND Name = @Name",
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
            CREATE TABLE TestTable (
                Id INT PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT GETUTCDATE()
            )
        ";

        var insertDataScript = @"
            INSERT INTO TestTable (Id, Name) VALUES (1, 'Test Item 1');
            INSERT INTO TestTable (Id, Name) VALUES (2, 'Test Item 2');
            INSERT INTO TestTable (Id, Name) VALUES (3, 'Test Item 3');
        ";

        // Run script with DbUp and use a custom journal table to avoid conflicts
        var upgradeEngine = DeployChanges.To
            .SqlDatabase(_fixture.ConnectionString)
            .WithScript("CreateTestTable", createTableScript)
            .WithScript("InsertTestData", insertDataScript)
            .JournalToSqlTable("dbo", "__CustomSchemaVersions")  // Use a different journal table for these tests
            .WithTransaction()
            .Build();

        var dbUpResult = upgradeEngine.PerformUpgrade();
        Assert.True(dbUpResult.Successful, $"DbUp script execution failed: {dbUpResult.Error}");

        // Step 2: Verify table and data exists using parameterized query
        using var connection = new SqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand("SELECT COUNT(*) FROM TestTable", connection);
        var queryResult = await command.ExecuteScalarAsync();
        var count = queryResult != null ? Convert.ToInt32(queryResult) : 0;

        Assert.Equal(3, count);

        // Step 3: Clean up (drop the test table)
        var dropTableScript = "DROP TABLE TestTable";

        var cleanupEngine = DeployChanges.To
            .SqlDatabase(_fixture.ConnectionString)
            .WithScript("DropTestTable", dropTableScript)
            .JournalToSqlTable("dbo", "__CustomSchemaVersions")  // Use the same custom journal table
            .WithTransaction()
            .Build();

        var cleanupResult = cleanupEngine.PerformUpgrade();
        Assert.True(cleanupResult.Successful, $"DbUp cleanup script execution failed: {cleanupResult.Error}");

        // Step 4: Verify table was dropped using parameterized query
        using var connection2 = new SqlConnection(_fixture.ConnectionString);
        await connection2.OpenAsync();

        using var command2 = new SqlCommand(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @TableName",
            connection2);

        command2.Parameters.AddWithValue("@TableName", "TestTable");

        var dropQueryResult = await command2.ExecuteScalarAsync();
        var tableExists = dropQueryResult != null ? Convert.ToInt32(dropQueryResult) : 0;

        Assert.Equal(0, tableExists);
    }
}



