using FsCheck;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class MoneyFuzzTests
{
    private static Arbitrary<decimal> NonNegativeDecimal() =>
        Gen.Choose(0, 999_999_999).Select(i => (decimal)i / 100m).ToArbitrary();

    private static Arbitrary<string> ValidCurrency() =>
        Gen.Elements("USD", "EUR", "GBP", "AUD", "NZD", "JPY", "CAD")
           .ToArbitrary();

    [Property]
    public Property NonNegativeAmount_AlwaysCreatesValidMoney()
    {
        return Prop.ForAll(NonNegativeDecimal(), ValidCurrency(),
            (amount, currency) =>
            {
                var money = Money.Create(amount, currency);
                return (money.Amount == amount && money.Currency == currency)
                    .Label($"Expected {amount} {currency}");
            });
    }

    [Property]
    public Property NegativeAmount_AlwaysThrows()
    {
        var negativeDecimal = Gen.Choose(1, 999_999).Select(i => -(decimal)i / 100m).ToArbitrary();
        return Prop.ForAll(negativeDecimal,
            amount =>
            {
                try
                { Money.Create(amount); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Property]
    public Property Addition_IsCommutative()
    {
        return Prop.ForAll(NonNegativeDecimal(), NonNegativeDecimal(),
            (a, b) =>
            {
                var moneyA = Money.Create(a, "USD");
                var moneyB = Money.Create(b, "USD");
                return moneyA.Add(moneyB).Amount == moneyB.Add(moneyA).Amount;
            });
    }

    [Property]
    public Property Addition_IsAssociative()
    {
        var bounded = Gen.Choose(0, 1_000_000).Select(i => (decimal)i / 100m).ToArbitrary();
        return Prop.ForAll(bounded, bounded, bounded,
            (a, b, c) =>
            {
                var mA = Money.Create(a, "USD");
                var mB = Money.Create(b, "USD");
                var mC = Money.Create(c, "USD");
                var left = mA.Add(mB).Add(mC).Amount;
                var right = mA.Add(mB.Add(mC)).Amount;
                return left == right;
            });
    }

    [Property]
    public Property SubtractIsInverseOfAdd()
    {
        var bounded = Gen.Choose(0, 999_999).Select(i => (decimal)i / 100m).ToArbitrary();
        return Prop.ForAll(bounded, bounded,
            (a, b) =>
            {
                var moneyA = Money.Create(a, "USD");
                var moneyB = Money.Create(b, "USD");
                return moneyA.Add(moneyB).Subtract(moneyB).Amount == a;
            });
    }

    [Property]
    public Property CurrencyCodeOver3Chars_AlwaysRejected()
    {
        var longCurrency = Gen.Elements("USDX", "EURO", "GBPP", "AUDD", "ABCDE", "TOOLONG")
            .ToArbitrary();
        return Prop.ForAll(longCurrency,
            currency =>
            {
                try
                { Money.Create(0m, currency); return false; }
                catch (ArgumentException) { return true; }
            });
    }
}
