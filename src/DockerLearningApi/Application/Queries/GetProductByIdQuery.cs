using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Application.ReadModels;
using MediatR;

namespace DockerLearningApi.Application.Queries;

public class GetProductByIdQuery : IQuery<ProductDto?>, IRequest<ProductDto?>
{
    public int Id { get; }

    public GetProductByIdQuery(int id)
    {
        Id = id;
    }
}

public class GetProductByIdQueryHandler : IQueryHandler<GetProductByIdQuery, ProductDto?>,
                                         IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IProductQueryService _queryService;
    private readonly ILogger<GetProductByIdQueryHandler> _logger;

    public GetProductByIdQueryHandler(
        IProductQueryService queryService,
        ILogger<GetProductByIdQueryHandler> logger)
    {
        _queryService = queryService;
        _logger = logger;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling GetProductByIdQuery for product {Id}", query.Id);
        
        var product = await _queryService.GetProductByIdAsync(query.Id);
        
        if (product == null)
        {
            _logger.LogWarning("Product with ID {Id} not found", query.Id);
            return null;
        }
            
        return MapToDtoFromReadModel(product);
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