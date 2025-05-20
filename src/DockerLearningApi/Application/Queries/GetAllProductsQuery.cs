using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Application.ReadModels;
using MediatR;
using Microsoft.Extensions.Logging;

namespace DockerLearningApi.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductDto>>, IRequest<IEnumerable<ProductDto>>
{
}

public class GetAllProductsQueryHandler : IQueryHandler<GetAllProductsQuery, IEnumerable<ProductDto>>, 
                                         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductDto>>
{
    private readonly IProductQueryService _queryService;
    private readonly ILogger<GetAllProductsQueryHandler> _logger;

    public GetAllProductsQueryHandler(
        IProductQueryService queryService,
        ILogger<GetAllProductsQueryHandler> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<IEnumerable<ProductDto>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling GetAllProductsQuery");
        
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