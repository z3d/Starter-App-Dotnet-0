using DockerLearning.Domain.ValueObjects;
using System;
using Xunit;

namespace DockerLearningApi.Tests.Domain;

public class MoneyTests
{
    [Fact]
    public void Create_WithValidAmount_ShouldCreateMoney()
    {
        // Arrange
        var amount = 10.99m;
        var currency = "USD";

        // Act
        var money = Money.Create(amount, currency);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal(currency, money.Currency);
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowArgumentException()
    {
        // Arrange
        var amount = -10.99m;
        var currency = "USD";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            Money.Create(amount, currency));
        Assert.Contains("Amount cannot be negative", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowArgumentException()
    {
        // Arrange
        var amount = 10.99m;
        var currency = string.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            Money.Create(amount, currency));
        Assert.Contains("Currency cannot be empty", exception.Message);
    }

    [Fact]
    public void FromDecimal_WithValidAmount_ShouldCreateMoneyWithDefaultCurrency()
    {
        // Arrange
        var amount = 10.99m;

        // Act
        var money = Money.FromDecimal(amount);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal("USD", money.Currency);
    }

    [Fact]
    public void Add_WithSameCurrency_ShouldAddAmounts()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.01m, "USD");
        var expectedAmount = 16.00m;

        // Act
        var result = money1.Add(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(10.00m, "EUR");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            money1.Add(money2));
        Assert.Contains("Cannot add money with different currencies", exception.Message);
    }

    [Fact]
    public void Subtract_WithSameCurrency_ShouldSubtractAmounts()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "USD");
        var expectedAmount = 5.00m;

        // Act
        var result = money1.Subtract(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Subtract_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "EUR");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            money1.Subtract(money2));
        Assert.Contains("Cannot subtract money with different currencies", exception.Message);
    }
}