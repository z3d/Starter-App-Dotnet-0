using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Interfaces;
using StarterApp.Api.Application.ReadModels;
using MediatR;
using Serilog;

namespace StarterApp.Api.Application.Queries;

public class GetCustomersQuery : IQuery<IEnumerable<CustomerDto>>, IRequest<IEnumerable<CustomerDto>>
{
}

public class GetCustomersQueryHandler : IQueryHandler<GetCustomersQuery, IEnumerable<CustomerDto>>,
                                      IRequestHandler<GetCustomersQuery, IEnumerable<CustomerDto>>
{
    private readonly ICustomerQueryService _queryService;

    public GetCustomersQueryHandler(ICustomerQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IEnumerable<CustomerDto>> Handle(GetCustomersQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetCustomersQuery");
        
        var customers = await _queryService.GetAllCustomersAsync();
        
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