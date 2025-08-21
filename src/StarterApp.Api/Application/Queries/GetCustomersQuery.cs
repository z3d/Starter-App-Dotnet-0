using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Application.Queries;

public class GetCustomersQuery : IQuery<IEnumerable<CustomerDto>>, IRequest<IEnumerable<CustomerDto>>
{
}

public class GetCustomersQueryHandler : IQueryHandler<GetCustomersQuery, IEnumerable<CustomerDto>>,
                                      IRequestHandler<GetCustomersQuery, IEnumerable<CustomerDto>>
{
    private readonly string _connectionString;

    public GetCustomersQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<IEnumerable<CustomerDto>> Handle(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomersQuery");
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        Log.Information("Retrieving all customers using Dapper");
        
        var sqlQuery = @"
            SELECT 
                Id, 
                Name, 
                Email, 
                DateCreated, 
                IsActive
            FROM Customers";

        var customers = await connection.QueryAsync<CustomerReadModel>(sqlQuery);
        
        return customers.Select(MapToDtoFromReadModel);
    }

    private static CustomerDto MapToDtoFromReadModel(CustomerReadModel readModel)
    {
        return new CustomerDto
        {
            Id = readModel.Id,
            Name = readModel.Name,
            Email = readModel.Email,
            DateCreated = readModel.DateCreated,
            IsActive = readModel.IsActive
        };
    }
}