using Dapper;
using StarterApp.Api.Application.ReadModels;
using Microsoft.Data.SqlClient;
using Serilog;

namespace StarterApp.Api.Application.Queries;
public class ProductQueryService : IProductQueryService
{
    private readonly string _connectionString;

    public ProductQueryService(IConfiguration configuration)
    {
        // Use the same connection string priority logic as Program.cs
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ??
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<IEnumerable<ProductReadModel>> GetAllProductsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        Log.Information("Retrieving all products using Dapper");

        var query = @"
            SELECT 
                Id, 
                Name, 
                Description, 
                PriceAmount, 
                PriceCurrency, 
                Stock, 
                LastUpdated
            FROM Products";

        return await connection.QueryAsync<ProductReadModel>(query);
    }

    public async Task<ProductReadModel?> GetProductByIdAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        Log.Information("Retrieving product {Id} using Dapper", id);

        var query = @"
            SELECT 
                Id, 
                Name, 
                Description, 
                PriceAmount, 
                PriceCurrency, 
                Stock, 
                LastUpdated
            FROM Products
            WHERE Id = @Id";

        return await connection.QueryFirstOrDefaultAsync<ProductReadModel>(query, new { Id = id });
    }
}
