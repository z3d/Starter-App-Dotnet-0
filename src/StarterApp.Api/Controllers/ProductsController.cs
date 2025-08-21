using StarterApp.Api.Application.Commands;
using StarterApp.Api.Application.DTOs;
using StarterApp.Api.Application.Queries;
using StarterApp.Api.Application.ReadModels;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace StarterApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // GET: api/Products
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductReadModel>>> GetProducts()
    {
        Log.Information("Getting all products");
        var query = new GetAllProductsQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // GET: api/Products/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ProductReadModel>> GetProduct(int id)
    {
        Log.Information("Getting product with ID: {Id}", id);
        var query = new GetProductByIdQuery(id);
        var result = await _mediator.Send(query);

        if (result == null)
        {
            Log.Warning("Product with ID: {Id} not found", id);
            return NotFound();
        }

        return Ok(result);
    }

    // POST: api/Products
    [HttpPost]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductCommand command)
    {
        Log.Information("Creating a new product");
        var result = await _mediator.Send(command);
        
        Log.Information("Created new product with ID: {Id}", result.Id);
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

        Log.Information("Updating product with ID: {Id}", id);
        
        try
        {
            await _mediator.Send(command);
            Log.Information("Updated product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Product with ID: {Id} not found during update", id);
            return NotFound();
        }

        return NoContent();
    }

    // DELETE: api/Products/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        Log.Information("Deleting product with ID: {Id}", id);
        
        try
        {
            await _mediator.Send(new DeleteProductCommand(id));
            Log.Information("Deleted product with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Product with ID: {Id} not found during delete", id);
            return NotFound();
        }

        return NoContent();
    }
}