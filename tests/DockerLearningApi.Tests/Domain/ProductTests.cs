namespace DockerLearningApi.Tests.Domain;

public class ProductTests
{
    [Fact]
    public void Create_WithValidInputs_ShouldCreateProduct()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        var price = Money.Create(10.99m);
        var stock = 100;

        // Act
        var product = Product.Create(name, description, price, stock);

        // Assert
        Assert.NotNull(product);
        Assert.Equal(name, product.Name);
        Assert.Equal(description, product.Description);
        Assert.Equal(price, product.Price);
        Assert.Equal(stock, product.Stock);
    }

    [Fact]
    public void Create_WithEmptyName_ShouldThrowArgumentException()
    {
        // Arrange
        var name = string.Empty;
        var description = "Test Description";
        var price = Money.Create(10.99m);
        var stock = 100;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            Product.Create(name, description, price, stock));
        Assert.Contains("Product name cannot be empty", exception.Message);
    }

    [Fact]
    public void Create_WithNullPrice_ShouldThrowArgumentNullException()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        Money? price = null;
        var stock = 100;

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() => 
            Product.Create(name, description, price!, stock));
        Assert.Equal("price", exception.ParamName);
    }

    [Fact]
    public void Create_WithNegativeStock_ShouldThrowArgumentException()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        var price = Money.Create(10.99m);
        var stock = -1;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            Product.Create(name, description, price, stock));
        Assert.Contains("Stock cannot be negative", exception.Message);
    }

    [Fact]
    public void UpdateDetails_WithValidInputs_ShouldUpdateProduct()
    {
        // Arrange
        var product = Product.Create("Original Name", "Original Description", Money.Create(10.99m), 100);
        var newName = "Updated Name";
        var newDescription = "Updated Description";
        var newPrice = Money.Create(19.99m);

        // Act
        product.UpdateDetails(newName, newDescription, newPrice);

        // Assert
        Assert.Equal(newName, product.Name);
        Assert.Equal(newDescription, product.Description);
        Assert.Equal(newPrice, product.Price);
    }

    [Fact]
    public void UpdateStock_WithValidQuantity_ShouldUpdateStock()
    {
        // Arrange
        var product = Product.Create("Test Product", "Test Description", Money.Create(10.99m), 100);
        var quantityToAdd = 50;
        var expectedStock = 150;

        // Act
        product.UpdateStock(quantityToAdd);

        // Assert
        Assert.Equal(expectedStock, product.Stock);
    }

    [Fact]
    public void UpdateStock_WithNegativeQuantityExceedingStock_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var product = Product.Create("Test Product", "Test Description", Money.Create(10.99m), 100);
        var quantityToRemove = -101;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            product.UpdateStock(quantityToRemove));
        Assert.Contains("Cannot reduce stock below zero", exception.Message);
    }
}