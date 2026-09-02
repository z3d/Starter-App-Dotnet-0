namespace StarterApp.Api.Application.ReadModels;

public class ProductReadModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    // Same names as ProductDto so a client that creates and then re-reads a product sees one shape.
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public int Stock { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}



