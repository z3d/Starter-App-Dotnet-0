using System.ComponentModel.DataAnnotations;
using StarterApp.Domain.ValueObjects;

namespace StarterApp.Api.Application.DTOs;

public class ProductDto
{
    public int Id { get; set; }
    
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string Description { get; set; } = string.Empty;
    
    [Range(0.01, double.MaxValue, ErrorMessage = "Price must be greater than 0")]
    public decimal Price { get; set; }
    
    [StringLength(Money.MaxCurrencyLength, ErrorMessage = "Currency code cannot exceed 3 characters")]
    public string Currency { get; set; } = "USD";
    
    [Range(0, int.MaxValue, ErrorMessage = "Stock cannot be negative")]
    public int Stock { get; set; }
    
    public DateTime LastUpdated { get; set; }
}