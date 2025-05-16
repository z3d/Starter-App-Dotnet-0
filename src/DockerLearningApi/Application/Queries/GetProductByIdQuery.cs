using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Interfaces;
using DockerLearning.Domain.Interfaces;
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
    private readonly IProductRepository _productRepository;

    public GetProductByIdQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery query, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(query.Id);
        
        if (product == null)
            return null;
            
        return new ProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Description = product.Description,
            Price = product.Price.Amount,
            Currency = product.Price.Currency,
            Stock = product.Stock,
            LastUpdated = product.LastUpdated
        };
    }
}