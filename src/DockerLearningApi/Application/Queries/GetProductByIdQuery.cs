using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Application.ReadModels;
using MediatR;
using Serilog;

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

    public GetProductByIdQueryHandler(IProductQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        Log.Information("Handling GetProductByIdQuery for product {Id}", query.Id);
        
        var product = await _queryService.GetProductByIdAsync(query.Id);
        
        if (product == null)
        {
            Log.Warning("Product with ID {Id} not found", query.Id);
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