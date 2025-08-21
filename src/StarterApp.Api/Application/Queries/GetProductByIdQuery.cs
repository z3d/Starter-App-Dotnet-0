using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;

namespace StarterApp.Api.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductReadModel?>, IRequest<ProductReadModel?>
{
    public int Id { get; }

    public GetProductByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductReadModel?>
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

    public async Task<ProductReadModel?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
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

        return product;
    }

    public async Task<ProductReadModel?> HandleAsync(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}



