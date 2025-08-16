using Dapper;
using StarterApp.Api.Application.ReadModels;
using Microsoft.Data.SqlClient;
using Serilog;

namespace StarterApp.Api.Application.Queries;

public class CustomerQueryService : ICustomerQueryService
{
    private readonly string _connectionString;

    public CustomerQueryService(IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("database");
        var dockerLearningConnection = configuration.GetConnectionString("DockerLearning");
        var sqlserverConnection = configuration.GetConnectionString("sqlserver");
        var defaultConnection = configuration.GetConnectionString("DefaultConnection");

        _connectionString = databaseConnection ?? dockerLearningConnection ?? sqlserverConnection ?? defaultConnection ?? 
            throw new InvalidOperationException("No connection string found. Checked: database, DockerLearning, sqlserver, DefaultConnection.");
    }

    public async Task<IEnumerable<CustomerReadModel>> GetAllCustomersAsync()
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        Log.Information("Retrieving all customers using Dapper");
        
        var query = @"
            SELECT 
                Id, 
                Name, 
                Email, 
                DateCreated, 
                IsActive
            FROM Customers";

        return await connection.QueryAsync<CustomerReadModel>(query);
    }

    public async Task<CustomerReadModel?> GetCustomerByIdAsync(int id)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        Log.Information("Retrieving customer {Id} using Dapper", id);
        
        var query = @"
            SELECT 
                Id, 
                Name, 
                Email, 
                DateCreated, 
                IsActive
            FROM Customers
            WHERE Id = @Id";

        return await connection.QueryFirstOrDefaultAsync<CustomerReadModel>(query, new { Id = id });
    }
}