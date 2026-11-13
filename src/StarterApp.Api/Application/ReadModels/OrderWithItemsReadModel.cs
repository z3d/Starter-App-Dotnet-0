namespace StarterApp.Api.Application.ReadModels;

public class OrderWithItemsReadModel : OrderReadModel
{
    public List<OrderItemReadModel> Items { get; set; } = [];
}

public class OrderItemReadModel
{
    public Guid OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPriceExcludingGst { get; set; }
    public decimal UnitPriceIncludingGst { get; set; }
    public decimal TotalPriceExcludingGst { get; set; }
    public decimal TotalPriceIncludingGst { get; set; }
    public decimal GstRate { get; set; }
    public string Currency { get; set; } = "USD";
}
