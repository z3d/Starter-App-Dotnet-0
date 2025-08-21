using StarterApp.Domain.Entities;
using StarterApp.Domain.ValueObjects;
using StarterApp.Tests.TestBuilders;
using System;
using Xunit;

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
        var product = ProductBuilder.AValidProduct()
            .WithName(name)
            .WithDescription(description)
            .WithPrice(price)
            .WithStock(stock)
            .Build();

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
            ProductBuilder.AValidProduct()
                .WithName(string.Empty)
                .Build());

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

        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Product(name, description, price!, stock));

        Assert.Equal("price", exception.ParamName);
    }

    [Fact]
    public void Create_WithNegativeStock_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductBuilder.AValidProduct()
                .WithStock(-1)
                .Build());

        Assert.Contains("must be a non-negative value", exception.Message);
    }

    [Fact]
    public void UpdateDetails_WithValidInputs_ShouldUpdateProduct()
    {
        // Arrange
        var product = ProductBuilder.AValidProduct()
            .WithName("Original Name")
            .WithDescription("Original Description")
            .WithPrice(10.99m)
            .Build();

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
        var initialStock = 100;
        var product = ProductBuilder.AValidProduct()
            .WithStock(initialStock)
            .Build();

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
        var product = ProductBuilder.AValidProduct()
            .WithStock(initialStock)
            .Build();

        var quantityToRemove = -101;

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            product.UpdateStock(quantityToRemove));

        Assert.Contains("Cannot reduce stock below zero", exception.Message);
    }
}
