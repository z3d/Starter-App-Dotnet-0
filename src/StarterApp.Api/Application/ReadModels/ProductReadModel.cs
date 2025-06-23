namespace StarterApp.Api.Application.ReadModels;

/// <summary>
/// Represents a product for read operations using Dapper
/// </summary>
public class ProductReadModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = "USD";
    public int Stock { get; set; }
    public DateTime LastUpdated { get; set; }
}