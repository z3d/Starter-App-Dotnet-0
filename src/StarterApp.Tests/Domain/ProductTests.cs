namespace StarterApp.Tests.Domain;

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
        var product = TestEntities.Product(name, description, price, stock);

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
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TestEntities.Product(name: string.Empty));

        Assert.Contains("cannot be an empty string", exception.Message);
    }

    [Fact]
    public void Create_WithNullPrice_ShouldThrowArgumentNullException()
    {
        // Arrange
        var name = "Test Product";
        var description = "Test Description";
        Money? price = null;
        var stock = 10;

        // Act & Assert — call the owner-aware ctor directly so the null reaches the guard
        // (TestEntities.Product coalesces a null price to a default for ergonomics).
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Product(name, description, price!, stock,
                OwnershipDefaults.LegacyOwnerSubject, OwnershipDefaults.LegacyTenantId));

        Assert.Equal("price", exception.ParamName);
    }

    [Fact]
    public void Create_WithNegativeStock_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            TestEntities.Product(stock: -1));

        Assert.Contains("must be a non-negative value", exception.Message);
    }

    [Fact]
    public void Create_WithNameExceedingMaxLength_ShouldThrowArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TestEntities.Product(name: new string('p', Product.MaxNameLength + 1)));

        Assert.Contains($"Product name cannot exceed {Product.MaxNameLength} characters", exception.Message);
    }

    [Fact]
    public void UpdateDetails_WithValidInputs_ShouldUpdateProduct()
    {
        // Arrange
        var product = TestEntities.Product("Original Name", "Original Description", Money.Create(10.99m));

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

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UpdateDetails_WithEmptyOrWhitespaceName_ShouldThrowArgumentException(string invalidName)
    {
        // Arrange
        var product = TestEntities.Product();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            product.UpdateDetails(invalidName, "Description", Money.Create(10.00m)));
    }

    [Fact]
    public void UpdateDetails_WithNullPrice_ShouldThrowArgumentNullException()
    {
        // Arrange
        var product = TestEntities.Product();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            product.UpdateDetails("Name", "Description", null!));
    }

    [Fact]
    public void UpdateDetails_WithDescriptionExceedingMaxLength_ShouldThrowArgumentException()
    {
        var product = TestEntities.Product();

        var exception = Assert.Throws<ArgumentException>(() =>
            product.UpdateDetails("Name", new string('d', Product.MaxDescriptionLength + 1), Money.Create(10.00m)));

        Assert.Contains($"Product description cannot exceed {Product.MaxDescriptionLength} characters", exception.Message);
    }

    [Fact]
    public void UpdateStock_WithValidQuantity_ShouldUpdateStock()
    {
        // Arrange
        var initialStock = 100;
        var product = TestEntities.Product(stock: initialStock);

        var quantityToAdd = 50;
        var expectedStock = initialStock + quantityToAdd;

        // Act
        product.UpdateStock(quantityToAdd);

        // Assert
        Assert.Equal(expectedStock, product.Stock);
    }

    [Fact]
    public void UpdateStock_WithNegativeQuantityExceedingStock_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var initialStock = 100;
        var product = TestEntities.Product(stock: initialStock);

        var quantityToRemove = -101;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            product.UpdateStock(quantityToRemove));

        Assert.Contains("Cannot reduce stock below zero", exception.Message);
    }
}
