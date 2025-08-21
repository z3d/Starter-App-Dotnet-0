using StarterApp.Domain.ValueObjects;
using System;
using Xunit;
using Xunit.Abstractions;
using Serilog;

namespace StarterApp.Tests.Domain;

public class MoneyTests
{
    private readonly ITestOutputHelper _output;

    public MoneyTests(ITestOutputHelper output)
    {
        _output = output;
        TestLoggerConfiguration.ConfigureTestLogging(output);
    }

    [Fact]
    public void Create_WithCurrencyExceedingMaxLength_ShouldThrowArgumentException()
    {
        // Arrange
        Log.Information("Testing money creation with currency exceeding max length");
        var amount = 10.99m;
        var currency = "USDT"; // 4 characters, exceeding the 3 character limit

        // Act & Assert
        Log.Debug("Attempting to create money with currency {Currency} that exceeds max length", currency);
        var exception = Assert.Throws<ArgumentException>(() =>
            Money.Create(amount, currency));
        Assert.Contains($"Currency code cannot exceed {Money.MaxCurrencyLength} characters", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("EU", true)]
    [InlineData("A", true)]
    [InlineData("USDT", false)]
    [InlineData("EURO", false)]
    public void Create_ValidatesCurrencyLength(string currency, bool shouldBeValid)
    {
        // Arrange
        Log.Information("Testing currency code validation with {Currency}", currency);
        var amount = 10.99m;

        if (shouldBeValid)
        {
            // Act - Should not throw for valid currency codes
            Log.Debug("Creating money with valid currency length: {Currency}", currency);
            var money = Money.Create(amount, currency);

            // Assert
            Assert.NotNull(money);
            Assert.Equal(currency, money.Currency);
            Log.Information("Successfully created money with currency: {Currency}", currency);
        }
        else
        {
            // Act & Assert - Should throw for invalid currency codes
            Log.Debug("Attempting to create money with invalid currency length: {Currency}", currency);
            var exception = Assert.Throws<ArgumentException>(() => Money.Create(amount, currency));
            Assert.Contains($"Currency code cannot exceed {Money.MaxCurrencyLength} characters", exception.Message);
            Log.Information("Exception correctly thrown for invalid currency: {Currency}", currency);
        }
    }

    [Fact]
    public void Create_WithValidAmount_ShouldCreateMoney()
    {
        // Arrange
        Log.Information("Testing money creation with valid amount");
        var amount = 10.99m;
        var currency = "USD";

        // Act
        Log.Debug("Creating money with amount {Amount} and currency {Currency}", amount, currency);
        var money = Money.Create(amount, currency);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal(currency, money.Currency);
        Log.Information("Money object successfully created with expected values");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowArgumentException()
    {
        // Arrange
        Log.Information("Testing money creation with negative amount");
        var amount = -10.99m;
        var currency = "USD";

        // Act & Assert
        Log.Debug("Attempting to create money with negative amount {Amount}", amount);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Money.Create(amount, currency));
        Assert.Contains("must be a non-negative value", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowArgumentException()
    {
        // Arrange
        Log.Information("Testing money creation with empty currency");
        var amount = 10.99m;
        var currency = string.Empty;

        // Act & Assert
        Log.Debug("Attempting to create money with empty currency");
        var exception = Assert.Throws<ArgumentException>(() =>
            Money.Create(amount, currency));
        Assert.Contains("cannot be an empty string", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void FromDecimal_WithValidAmount_ShouldCreateMoneyWithDefaultCurrency()
    {
        // Arrange
        Log.Information("Testing FromDecimal method with valid amount");
        var amount = 10.99m;

        // Act
        Log.Debug("Creating money from decimal {Amount}", amount);
        var money = Money.FromDecimal(amount);

        // Assert
        Assert.NotNull(money);
        Assert.Equal(amount, money.Amount);
        Assert.Equal("USD", money.Currency);
        Log.Information("Money object successfully created with default currency");
    }

    [Fact]
    public void Add_WithSameCurrency_ShouldAddAmounts()
    {
        // Arrange
        Log.Information("Testing addition of money with same currency");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.01m, "USD");
        var expectedAmount = 16.00m;

        // Act
        Log.Debug("Adding {Amount1} {Currency1} + {Amount2} {Currency2}",
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var result = money1.Add(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
        Log.Information("Addition successful: {Result} {Currency}", result.Amount, result.Currency);
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Log.Information("Testing addition of money with different currencies");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(10.00m, "EUR");

        // Act & Assert
        Log.Debug("Attempting to add {Amount1} {Currency1} + {Amount2} {Currency2}",
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            money1.Add(money2));
        Assert.Contains("Cannot add money with different currencies", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }

    [Fact]
    public void Subtract_WithSameCurrency_ShouldSubtractAmounts()
    {
        // Arrange
        Log.Information("Testing subtraction of money with same currency");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "USD");
        var expectedAmount = 5.00m;

        // Act
        Log.Debug("Subtracting {Amount1} {Currency1} - {Amount2} {Currency2}",
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var result = money1.Subtract(money2);

        // Assert
        Assert.Equal(expectedAmount, result.Amount);
        Assert.Equal("USD", result.Currency);
        Log.Information("Subtraction successful: {Result} {Currency}", result.Amount, result.Currency);
    }

    [Fact]
    public void Subtract_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        Log.Information("Testing subtraction of money with different currencies");
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "EUR");

        // Act & Assert
        Log.Debug("Attempting to subtract {Amount1} {Currency1} - {Amount2} {Currency2}",
            money1.Amount, money1.Currency, money2.Amount, money2.Currency);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            money1.Subtract(money2));
        Assert.Contains("Cannot subtract money with different currencies", exception.Message);
        Log.Information("Exception correctly thrown with message: {Message}", exception.Message);
    }
}
