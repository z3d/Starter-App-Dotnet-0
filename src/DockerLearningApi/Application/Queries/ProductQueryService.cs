using Dapper;
using DockerLearningApi.Application.ReadModels;
using Microsoft.Data.SqlClient;

namespace DockerLearningApi.Application.Queries;

/// <summary>
/// Service for product read operations using Dapper
/// </summary>
public class ProductQueryService : IProductQueryService
{
    private readonly string _connectionString;
    private readonly ILogger<ProductQueryService> _logger;

    public ProductQueryService(IConfiguration configuration, ILogger<ProductQueryService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? 
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        _logger = logger;
    }

    public async Task<IEnumerable<ProductReadModel>> GetAllProductsAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        _logger.LogInformation("Retrieving all products using Dapper");
        
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

        _logger.LogInformation("Retrieving product {Id} using Dapper", id);
        
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