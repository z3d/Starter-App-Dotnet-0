namespace StarterApp.Api.Application.Commands;

public class CreateOrderItemCommand
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
