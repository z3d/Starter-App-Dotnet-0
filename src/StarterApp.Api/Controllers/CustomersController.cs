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
public class CustomersController : ControllerBase
{
    private readonly IMediator _mediator;

    public CustomersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CustomerReadModel>>> GetCustomers()
    {
        Log.Information("Getting all customers");
        var query = new GetCustomersQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerReadModel>> GetCustomer(int id)
    {
        Log.Information("Getting customer with ID: {Id}", id);
        var query = new GetCustomerQuery(id);
        var result = await _mediator.Send(query);

        if (result == null)
        {
            Log.Warning("Customer with ID: {Id} not found", id);
            return NotFound();
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerDto>> CreateCustomer(CreateCustomerCommand command)
    {
        Log.Information("Creating a new customer");
        var result = await _mediator.Send(command);

        Log.Information("Created new customer with ID: {Id}", result.Id);
        return CreatedAtAction(nameof(GetCustomer), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(int id, UpdateCustomerCommand command)
    {
        if (id != command.Id)
        {
            return BadRequest("ID in URL does not match ID in request body");
        }

        Log.Information("Updating customer with ID: {Id}", id);

        try
        {
            await _mediator.Send(command);
            Log.Information("Updated customer with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Customer with ID: {Id} not found during update", id);
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        Log.Information("Deleting customer with ID: {Id}", id);

        try
        {
            await _mediator.Send(new DeleteCustomerCommand { Id = id });
            Log.Information("Deleted customer with ID: {Id}", id);
        }
        catch (KeyNotFoundException)
        {
            Log.Warning("Customer with ID: {Id} not found during delete", id);
            return NotFound();
        }

        return NoContent();
    }
}
