namespace StarterApp.Tests.Application.DTOs;

public class ProductDtoTests
{
    [Fact]
    public void ProductDto_WithValidData_ShouldPassValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = "Valid Product",
            Description = "Valid description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Log.Information("Validating ProductDto with valid data");
        Assert.True(isValid);
        Assert.Empty(validationResults);
    }
    
    [Fact]
    public void ProductDto_WithLongName_ShouldFailValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = new string('A', 101), // 101 characters
            Description = "Valid description",
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        Log.Information("Validating ProductDto with name that exceeds max length");
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Name", validationResults[0].MemberNames);
        Assert.Contains("cannot exceed 100 characters", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
    
    [Fact]
    public void ProductDto_WithLongDescription_ShouldFailValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = "Valid Product",
            Description = new string('A', 501), // 501 characters
            Price = 10.99m,
            Currency = "USD",
            Stock = 100
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        Log.Information("Validating ProductDto with description that exceeds max length");
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Description", validationResults[0].MemberNames);
        Assert.Contains("cannot exceed 500 characters", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
    
    [Fact]
    public void ProductDto_WithNegativePrice_ShouldFailValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = "Valid Product",
            Description = "Valid description",
            Price = -10.99m,
            Currency = "USD",
            Stock = 100
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        Log.Information("Validating ProductDto with negative price");
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Price", validationResults[0].MemberNames);
        Assert.Contains("Price must be greater than 0", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
    
    [Fact]
    public void ProductDto_WithLongCurrency_ShouldFailValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = "Valid Product",
            Description = "Valid description",
            Price = 10.99m,
            Currency = "USDT", // 4 characters, exceeding the 3 character limit
            Stock = 100
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        Log.Information("Validating ProductDto with currency that exceeds max length");
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Currency", validationResults[0].MemberNames);
        Assert.Contains("Currency code cannot exceed 3 characters", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
    
    [Fact]
    public void ProductDto_WithNegativeStock_ShouldFailValidation()
    {
        // Arrange
        var productDto = new ProductDto
        {
            Name = "Valid Product",
            Description = "Valid description",
            Price = 10.99m,
            Currency = "USD",
            Stock = -10
        };
        
        var validationContext = new ValidationContext(productDto);
        var validationResults = new List<ValidationResult>();
        
        // Act
        Log.Information("Validating ProductDto with negative stock");
        var isValid = Validator.TryValidateObject(productDto, validationContext, validationResults, true);
        
        // Assert
        Assert.False(isValid);
        Assert.Single(validationResults);
        Assert.Contains("Stock", validationResults[0].MemberNames);
        Assert.Contains("Stock cannot be negative", validationResults[0].ErrorMessage);
        Log.Information("Validation correctly failed with message: {Message}", validationResults[0].ErrorMessage);
    }
}