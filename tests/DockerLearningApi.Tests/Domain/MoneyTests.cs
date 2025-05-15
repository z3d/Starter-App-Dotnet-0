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
        money.Should().NotBeNull();
        money.Amount.Should().Be(amount);
        money.Currency.Should().Be(currency);
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowArgumentException()
    {
        // Arrange
        var amount = -10.99m;
        var currency = "USD";

        // Act & Assert
        var act = () => Money.Create(amount, currency);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Amount cannot be negative*");
    }

    [Fact]
    public void Create_WithEmptyCurrency_ShouldThrowArgumentException()
    {
        // Arrange
        var amount = 10.99m;
        var currency = string.Empty;

        // Act & Assert
        var act = () => Money.Create(amount, currency);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Currency cannot be empty*");
    }

    [Fact]
    public void FromDecimal_WithValidAmount_ShouldCreateMoneyWithDefaultCurrency()
    {
        // Arrange
        var amount = 10.99m;

        // Act
        var money = Money.FromDecimal(amount);

        // Assert
        money.Should().NotBeNull();
        money.Amount.Should().Be(amount);
        money.Currency.Should().Be("USD");
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
        result.Amount.Should().Be(expectedAmount);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(10.00m, "EUR");

        // Act & Assert
        var act = () => money1.Add(money2);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Cannot add money with different currencies*");
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
        result.Amount.Should().Be(expectedAmount);
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public void Subtract_WithDifferentCurrencies_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var money1 = Money.Create(10.99m, "USD");
        var money2 = Money.Create(5.99m, "EUR");

        // Act & Assert
        var act = () => money1.Subtract(money2);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Cannot subtract money with different currencies*");
    }
}