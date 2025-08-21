using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductDto>>, IRequest<IEnumerable<ProductDto>>
{
}

public class GetAllProductsQueryHandler : IQueryHandler<GetAllProductsQuery, IEnumerable<ProductDto>>, 
                                         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductDto>>
{
    private readonly string _connectionString;

    public GetAllProductsQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<IEnumerable<ProductDto>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetAllProductsQuery");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        Log.Information("Retrieving all products using Dapper");
        
        var sqlQuery = @"
            SELECT 
                Id, 
                Name, 
                Description, 
                PriceAmount, 
                PriceCurrency, 
                Stock, 
                LastUpdated
            FROM Products";

        var products = await connection.QueryAsync<ProductReadModel>(sqlQuery);
        
        return products.Select(MapToDtoFromReadModel);
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