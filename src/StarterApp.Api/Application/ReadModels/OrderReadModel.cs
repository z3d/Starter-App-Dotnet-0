namespace StarterApp.Api.Application.ReadModels;

public class OrderReadModel
{
    public Guid Id { get; set; }
    public int CustomerId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalExcludingGst { get; set; }
    public decimal TotalIncludingGst { get; set; }
    public decimal TotalGstAmount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTimeOffset LastUpdated { get; set; }
}
