using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductReadModel>>, IRequest<IEnumerable<ProductReadModel>>
{
}

public class GetAllProductsQueryHandler : IQueryHandler<GetAllProductsQuery, IEnumerable<ProductReadModel>>,
                                         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductReadModel>>
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

    public async Task<IEnumerable<ProductReadModel>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
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

        return products;
    }

    public async Task<IEnumerable<ProductReadModel>> HandleAsync(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        return await Handle(query, cancellationToken);
    }
}



