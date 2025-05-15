using DockerLearningApi.Application.Commands;
using DockerLearningApi.Application.DTOs;
using DockerLearningApi.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DockerLearningApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IMediator mediator, ILogger<ProductsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    // GET: api/Products
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts()
    {
        _logger.LogInformation("Getting all products");
        var query = new GetAllProductsQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // GET: api/Products/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetProduct(int id)
    {
        _logger.LogInformation("Getting product with ID: {Id}", id);
        var query = new GetProductByIdQuery(id);
        var result = await _mediator.Send(query);

        if (result == null)
        {
            _logger.LogWarning("Product with ID: {Id} not found", id);
            return NotFound();
        }

        return Ok(result);
    }

    // POST: api/Products
    [HttpPost]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductCommand command)
    {
        _logger.LogInformation("Creating a new product");
        var result = await _mediator.Send(command);
        
        _logger.LogInformation("Created new product with ID: {Id}", result.Id);
        return CreatedAtAction(nameof(GetProduct), new { id = result.Id }, result);
    }

    // PUT: api/Products/5
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, UpdateProductCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("ID in URL does not match ID in request body");
        }

        _logger.LogInformation("Updating product with ID: {Id}", id);
        
        try
        {
            await _mediator.Send(command);
            _logger.LogInformation("Updated product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Product with ID: {Id} not found during update", id);
            return NotFound();
        }

        return NoContent();
    }

    // DELETE: api/Products/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        _logger.LogInformation("Deleting product with ID: {Id}", id);
        
        try
        {
            await _mediator.Send(new DeleteProductCommand(id));
            _logger.LogInformation("Deleted product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            _logger.LogWarning("Product with ID: {Id} not found during delete", id);
            return NotFound();
        }

        return NoContent();
    }
}