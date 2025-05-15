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
        product.Should().NotBeNull();
        product.Name.Should().Be(name);
        product.Description.Should().Be(description);
        product.Price.Should().Be(price);
        product.Stock.Should().Be(stock);
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
        var act = () => Product.Create(name, description, price, stock);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Product name cannot be empty*");
    }

    [Fact]
    public void Create_WithNullPrice_ShouldThrowArgumentNullException()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        Money price = null;
        var stock = 100;

        // Act & Assert
        var act = () => Product.Create(name, description, price, stock);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("price");
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
        var act = () => Product.Create(name, description, price, stock);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Stock cannot be negative*");
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
        product.Name.Should().Be(newName);
        product.Description.Should().Be(newDescription);
        product.Price.Should().Be(newPrice);
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
        product.Stock.Should().Be(expectedStock);
    }

    [Fact]
    public void UpdateStock_WithNegativeQuantityExceedingStock_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var product = Product.Create("Test Product", "Test Description", Money.Create(10.99m), 100);
        var quantityToRemove = -101;

        // Act & Assert
        var act = () => product.UpdateStock(quantityToRemove);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Cannot reduce stock below zero*");
    }
}