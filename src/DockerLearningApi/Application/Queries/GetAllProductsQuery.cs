using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearningApi.Domain.Interfaces;
using MediatR;

namespace DockerLearningApi.Application.Queries;

public class GetAllProductsQuery : IQuery<IEnumerable<ProductDto>>, IRequest<IEnumerable<ProductDto>>
{
}

public class GetAllProductsQueryHandler : IQueryHandler<GetAllProductsQuery, IEnumerable<ProductDto>>, 
                                         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductDto>>
{
    private readonly IProductRepository _productRepository;

    public GetAllProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<IEnumerable<ProductDto>> Handle(GetAllProductsQuery query, CancellationToken cancellationToken)
    {
        var products = await _productRepository.GetAllAsync();
        
        return products.Select(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Price = p.Price.Amount,
            Currency = p.Price.Currency,
            Stock = p.Stock,
            LastUpdated = p.LastUpdated
        });
    }
}