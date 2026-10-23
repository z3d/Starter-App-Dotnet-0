using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace StarterApp.Tests.Domain;

public class MoneyScaleTests
{
    [Theory]
    [InlineData(10.989, "USD")]
    [InlineData(0.001, "AUD")]
    [InlineData(1.5, "JPY")]
    [InlineData(1.2345, "KWD")]
    [InlineData(2.675, "EUR")]
    public void Create_WithMoreDecimalPlacesThanTheCurrencyHas_IsRefusedNotRounded(decimal amount, string currency)
    {
        var exception = Assert.Throws<ArgumentException>(() => Money.Create(amount, currency));
        Assert.Contains("never rounded", exception.Message);
    }

    [Theory]
    [InlineData(12.5000, "USD", 12.5)]
    [InlineData(100.00, "JPY", 100)]
    [InlineData(1.234, "KWD", 1.234)]
    public void Create_AtTheCurrencysScale_KeepsTheAmountExactly(decimal amount, string currency, decimal expected)
    {
        Assert.Equal(expected, Money.Create(amount, currency).Amount);
    }

    [Fact]
    public void Create_HasNoDefaultCurrency()
    {
        var creates = typeof(Money).GetMethods().Where(m => m.Name == "Create").ToList();

        Assert.NotEmpty(creates);
        Assert.All(creates, create => Assert.DoesNotContain(create.GetParameters(), p => p.HasDefaultValue));
        Assert.Null(typeof(Money).GetMethod("FromDecimal"));
    }

    private static readonly (string Currency, int Places)[] Currencies =
        [("AUD", 2), ("USD", 2), ("EUR", 2), ("JPY", 0), ("KRW", 0), ("KWD", 3), ("BHD", 3), ("CLF", 4)];

    private static Arbitrary<(string Currency, int Places, decimal Amount)> AmountAtScale() =>
        Gen.Elements(Currencies)
            .SelectMany(c => Gen.Choose(0, 999_999_999).Select(units => (c.Currency, c.Places, Amount: units / Pow10(c.Places))))
            .ToArbitrary();

    private static decimal Pow10(int places) => Enumerable.Repeat(10m, places).Aggregate(1m, (product, ten) => product * ten);

    [Property(MaxTest = 500)]
    public Property AnAmountAtTheCurrencysScale_RoundTripsExactly() =>
        Prop.ForAll(AmountAtScale(), sample =>
        {
            var money = Money.Create(sample.Amount, sample.Currency);
            return (money.Amount == sample.Amount && money.Currency == sample.Currency)
                .Label($"{sample.Amount} {sample.Currency} came back as {money}");
        });

    [Property(MaxTest = 500)]
    public Property AnAmountPastTheCurrencysScale_IsAlwaysRefused()
    {
        var offScale = Gen.Elements(Currencies)
            .SelectMany(c => Gen.Choose(0, 999_999).SelectMany(units => Gen.Choose(1, 9)
                .Select(extra => (c.Currency, Amount: units / Pow10(c.Places) + extra / Pow10(c.Places + 1)))))
            .ToArbitrary();

        return Prop.ForAll(offScale, sample =>
        {
            try
            {
                var money = Money.Create(sample.Amount, sample.Currency);
                return false.Label($"{sample.Amount} {sample.Currency} was accepted as {money}");
            }
            catch (ArgumentException)
            {
                return true.ToProperty();
            }
        });
    }
}
