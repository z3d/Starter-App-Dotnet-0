using StarterApp.Api.Application.ReadModels;
using StarterApp.Api.Application.Interfaces;

namespace StarterApp.Api.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductDto>>, IRequest<IEnumerable<ProductDto>>
{
}

public class GetAllProductsQueryHandler : IQueryHandler<GetAllProductsQuery, IEnumerable<ProductDto>>, 
                                         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductDto>>
{
    private readonly IProductQueryService _queryService;

    public GetAllProductsQueryHandler(IProductQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<IEnumerable<ProductDto>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetAllProductsQuery");
        
        var products = await _queryService.GetAllProductsAsync();
        
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