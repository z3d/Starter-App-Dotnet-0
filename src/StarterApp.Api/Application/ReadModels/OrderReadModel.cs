namespace StarterApp.Api.Application.ReadModels;

public class OrderReadModel
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalExcludingGst { get; set; }
    public decimal TotalIncludingGst { get; set; }
    public decimal TotalGstAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime LastUpdated { get; set; }
}

public class OrderItemReadModel
{
    public int OrderId { get; set; }
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

public class OrderWithItemsReadModel : OrderReadModel
{
    public List<OrderItemReadModel> Items { get; set; } = [];
}



