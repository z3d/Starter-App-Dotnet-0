using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;
using Dapper;
using Microsoft.Data.SqlClient;

namespace StarterApp.Api.Application.Queries;

public class GetCustomerQuery : IQuery<CustomerDto?>, IRequest<CustomerDto?>
{
    public int Id { get; }

    public GetCustomerQuery(int id)
    {
        Id = id;
    }
}

public class GetCustomerQueryHandler : IQueryHandler<GetCustomerQuery, CustomerDto?>,
                                     IRequestHandler<GetCustomerQuery, CustomerDto?>
{
    private readonly string _connectionString;

    public GetCustomerQueryHandler(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<CustomerDto?> Handle(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomerQuery for customer {Id}", query.Id);
        
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        Log.Information("Retrieving customer {Id} using Dapper", query.Id);
        
        var sqlQuery = @"
            SELECT 
                Id, 
                Name, 
                Email, 
                DateCreated, 
                IsActive
            FROM Customers
            WHERE Id = @Id";

        var customer = await connection.QueryFirstOrDefaultAsync<CustomerReadModel>(sqlQuery, new { Id = query.Id });
        
        if (customer == null)
        {
            Log.Warning("Customer with ID {Id} not found", query.Id);
            return null;
        }
            
        return MapToDtoFromReadModel(customer);
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