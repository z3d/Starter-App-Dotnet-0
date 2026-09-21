namespace StarterApp.Tests.Domain;

public class MoneyArithmeticTests
{
    [Fact]
    public void Add_WhenResultExceedsMaxAmount_ShouldThrowLikeCreate()
    {
        // Add used to call the private constructor and was the one construction path that could
        // produce a Money above MaxAmount (numeric(18,2)); it now routes through Create().
        var atCeiling = Money.Create(Money.MaxAmount);

        Assert.Throws<ArgumentOutOfRangeException>(() => atCeiling.Add(Money.Create(1m)));
    }

    [Fact]
    public void Add_And_Subtract_WithNull_ShouldThrowArgumentNullException()
    {
        var money = Money.Create(10m);

        Assert.Throws<ArgumentNullException>(() => money.Add(null!));
        Assert.Throws<ArgumentNullException>(() => money.Subtract(null!));
    }
}
