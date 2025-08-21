using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductDto?>, IRequest<ProductDto?>
{
    public int Id { get; }

    public GetProductByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto?>,
                                         IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly string _connectionString;

    public GetProductByIdQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetProductByIdQuery for product {Id}", query.Id);
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        Log.Information("Retrieving product {Id} using Dapper", query.Id);
        
        var sqlQuery = @"
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

        var product = await connection.QueryFirstOrDefaultAsync<ProductReadModel>(sqlQuery, new { Id = query.Id });
        
        if (product == null)
        {
            Log.Warning("Product with ID {Id} not found", query.Id);
            return null;
        }
            
        return MapToDtoFromReadModel(product);
    }

    private static ProductDto MapToDtoFromReadModel(ProductReadModel readModel)
    {
        return new ProductDto
        {
            Id = readModel.Id,
            Name = readModel.Name,
            Description = readModel.Description,
            Price = readModel.PriceAmount,
            Currency = readModel.PriceCurrency,
            Stock = readModel.Stock,
            LastUpdated = readModel.LastUpdated
        };
    }
}