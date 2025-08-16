using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;
using MediatR;
using Serilog;

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
    private readonly ICustomerQueryService _queryService;

    public GetCustomerQueryHandler(ICustomerQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<CustomerDto?> Handle(GetCustomerQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomerQuery for customer {Id}", query.Id);
        
        var customer = await _queryService.GetCustomerByIdAsync(query.Id);
        
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