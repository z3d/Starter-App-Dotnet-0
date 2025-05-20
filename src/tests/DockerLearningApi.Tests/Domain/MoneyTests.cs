using DockerLearning.Domain.ValueObjects;
using System;
using Xunit;
using Xunit.Abstractions;
using Serilog;

namespace DockerLearningApi.Tests.Domain;

public class MoneyTests
{
    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;

    public MoneyTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = TestLoggerConfiguration.CreateLogger(output);
    }

    [Fact]
    public void Create_WithValidAmount_ShouldCreateMoney()
    {
        // Arrange
        _logger.Information("Testing money creation with valid amount");
        var amount = 10.99m;
        var currency = "USD";

        // Act
        _logger.Debug("Creating money with amount {Amount} and currency {Currency}", amount, currency);
        var money = Money.Create(amount, currency);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal(currency, money.Currency);
        _logger.Information("Money object successfully created with expected values");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowArgumentException()
    {
        // Arrange
        _logger.Information("Testing money creation with negative amount");
        var amount = -10.99m;
        var currency = "USD";

        // Act & Assert
        _logger.Debug("Attempting to create money with negative amount {Amount}", amount);
        var exception = Assert.Throws<ArgumentException>(() => 
            Money.Create(amount, currency));
        Assert.Contains("Amount cannot be negative", exception.Message);
        _logger.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowArgumentException()
    {
        // Arrange
        _logger.Information("Testing money creation with empty currency");
        var amount = 10.99m;
        var currency = string.Empty;

        // Act & Assert
        _logger.Debug("Attempting to create money with empty currency");
        var exception = Assert.Throws<ArgumentException>(() => 
            Money.Create(amount, currency));
        Assert.Contains("Currency cannot be empty", exception.Message);
        _logger.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void FromDecimal_WithValidAmount_ShouldCreateMoneyWithDefaultCurrency()
    {
        // Arrange
        _logger.Information("Testing FromDecimal method with valid amount");
        var amount = 10.99m;

        // Act
        _logger.Debug("Creating money from decimal {Amount}", amount);
        var money = Money.FromDecimal(amount);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal("USD", money.Currency);
        _logger.Information("Money object successfully created with default currency");
    }

    [Fact]
    public void Add_WithSameCurrency_ShouldAddAmounts()
    {
        // Arrange
        _logger.Information("Testing addition of money with same currency");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.01m, "USD");
        var expectedAmount = 16.00m;

        // Act
        _logger.Debug("Adding {Amount1} {Currency1} + {Amount2} {Currency2}", 
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var result = money1.Add(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
        _logger.Information("Addition successful: {Result} {Currency}", result.Amount, result.Currency);
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _logger.Information("Testing addition of money with different currencies");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(10.00m, "EUR");

        // Act & Assert
        _logger.Debug("Attempting to add {Amount1} {Currency1} + {Amount2} {Currency2}", 
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var exception = Assert.Throws<InvalidOperationException>(() => 
            money1.Add(money2));
        Assert.Contains("Cannot add money with different currencies", exception.Message);
        _logger.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Subtract_WithSameCurrency_ShouldSubtractAmounts()
    {
        // Arrange
        _logger.Information("Testing subtraction of money with same currency");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "USD");
        var expectedAmount = 5.00m;

        // Act
        _logger.Debug("Subtracting {Amount1} {Currency1} - {Amount2} {Currency2}", 
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var result = money1.Subtract(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
        _logger.Information("Subtraction successful: {Result} {Currency}", result.Amount, result.Currency);
    }

    [Fact]
    public void Subtract_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _logger.Information("Testing subtraction of money with different currencies");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "EUR");

        // Act & Assert
        _logger.Debug("Attempting to subtract {Amount1} {Currency1} - {Amount2} {Currency2}", 
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var exception = Assert.Throws<InvalidOperationException>(() => 
            money1.Subtract(money2));
        Assert.Contains("Cannot subtract money with different currencies", exception.Message);
        _logger.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }
}