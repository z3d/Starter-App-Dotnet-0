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
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    // GET: api/Orders/5
    [HttpGet("{id}")]
    public async Task<ActionResult<OrderWithItemsReadModel>> GetOrder(int id)
    {
        Log.Information("Getting order with ID: {Id}", id);
        var query = new GetOrderByIdQuery { Id = id };
        var result = await _mediator.Send(query);

        if (result == null)
        {
            Log.Warning("Order with ID: {Id} not found", id);
            return NotFound();
        }

        return Ok(result);
    }

    // GET: api/Orders/customer/5
    [HttpGet("customer/{customerId}")]
    public async Task<ActionResult<IEnumerable<OrderReadModel>>> GetOrdersByCustomer(int customerId)
    {
        Log.Information("Getting orders for customer: {CustomerId}", customerId);
        var query = new GetOrdersByCustomerQuery { CustomerId = customerId };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // GET: api/Orders/status/Pending
    [HttpGet("status/{status}")]
    public async Task<ActionResult<IEnumerable<OrderReadModel>>> GetOrdersByStatus(string status)
    {
        Log.Information("Getting orders with status: {Status}", status);
        var query = new GetOrdersByStatusQuery { Status = status };
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    // POST: api/Orders
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderCommand command)
    {
        Log.Information("Creating a new order for customer: {CustomerId}", command.CustomerId);

        var result = await _mediator.Send(command);
        Log.Information("Created new order with ID: {Id}", result.Id);
        return CreatedAtAction(nameof(GetOrder), new { id = result.Id }, result);
    }

    // PUT: api/Orders/5/status
    [HttpPut("{id}/status")]
    public async Task<ActionResult<OrderDto>> UpdateOrderStatus(int id, UpdateOrderStatusCommand command)
    {
        if (id != command.OrderId)
        {
            return BadRequest("ID in URL does not match ID in request body");
        }

        Log.Information("Updating order {Id} status to: {Status}", id, command.Status);

        try
        {
            var result = await _mediator.Send(command);
            Log.Information("Updated order {Id} status to: {Status}", id, command.Status);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Warning("Order with ID: {Id} not found during status update: {Message}", id, ex.Message);
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("Invalid status transition for order {Id}: {Message}", id, ex.Message);
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            Log.Warning("Invalid status value for order {Id}: {Message}", id, ex.Message);
            return BadRequest(ex.Message);
        }
    }

    // POST: api/Orders/5/cancel
    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<OrderDto>> CancelOrder(int id)
    {
        Log.Information("Cancelling order with ID: {Id}", id);

        try
        {
            var command = new CancelOrderCommand { OrderId = id };
            var result = await _mediator.Send(command);
            Log.Information("Cancelled order with ID: {Id}", id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            Log.Warning("Order with ID: {Id} not found during cancellation: {Message}", id, ex.Message);
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning("Cannot cancel order {Id}: {Message}", id, ex.Message);
            return BadRequest(ex.Message);
        }
    }
}
