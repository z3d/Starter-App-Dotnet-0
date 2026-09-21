using FsCheck;
using FsCheck.Fluent;
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
    public Property Subtract_NeverProducesNegativeAmount()
    {
        var bounded = Gen.Choose(0, 999_999).Select(i => (decimal)i / 100m).ToArbitrary();
        return Prop.ForAll(bounded, bounded,
            (a, b) =>
            {
                var moneyA = Money.Create(a, "USD");
                var moneyB = Money.Create(b, "USD");
                if (a >= b)
                    return moneyA.Subtract(moneyB).Amount >= 0;
                try
                { moneyA.Subtract(moneyB); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    [Property]
    public Property CurrencyCodeNotExactly3Letters_AlwaysRejected()
    {
        var longCurrency = Gen.Elements("U", "US", "USDX", "EURO", "GBPP", "AUDD", "ABCDE", "TOOLONG", "12!", "US1", "A$D")
            .ToArbitrary();
        return Prop.ForAll(longCurrency,
            currency =>
            {
                try
                { Money.Create(0m, currency); return false; }
                catch (ArgumentException) { return true; }
            });
    }

    // ---- P1b: MaxAmount boundary ----

    [Fact]
    public void AmountAtMaxAmount_IsAccepted()
    {
        var money = Money.Create(Money.MaxAmount, "USD");
        Assert.Equal(Money.MaxAmount, money.Amount);
    }

    [Property(MaxTest = 500)]
    public Property AmountAtOrBelowMaxAmount_IsAccepted()
    {
        // Straddle MaxAmount from below: pick a cents offset back from the documented ceiling.
        // Money.Create rounds to 2dp, so stay on whole-cent values to keep the boundary exact.
        var nearMax = Gen.Choose(0, 1_000_000)
            .Select(offset => Money.MaxAmount - ((decimal)offset / 100m))
            .ToArbitrary();
        return Prop.ForAll(nearMax,
            amount =>
            {
                var money = Money.Create(amount, "USD");
                return (money.Amount == amount && money.Amount <= Money.MaxAmount)
                    .Label($"amount={amount} should create valid Money <= MaxAmount={Money.MaxAmount}");
            });
    }

    [Property(MaxTest = 500)]
    public Property AmountAboveMaxAmount_AlwaysThrows()
    {
        // > MaxAmount must throw the documented ArgumentOutOfRangeException (Money.cs ThrowIfGreaterThan).
        var overMax = Gen.Choose(1, 1_000_000)
            .Select(offset => Money.MaxAmount + ((decimal)offset / 100m))
            .ToArbitrary();
        return Prop.ForAll(overMax,
            amount =>
            {
                try
                { Money.Create(amount, "USD"); return false; }
                catch (ArgumentOutOfRangeException) { return true; }
            });
    }

    // ---- P2a: cross-currency arithmetic and currency-code validation ----

    private static Arbitrary<(string A, string B)> DistinctCurrencyPair() =>
        Gen.Elements("USD", "EUR", "GBP", "AUD", "NZD", "JPY", "CAD")
            .SelectMany(a => Gen.Elements("USD", "EUR", "GBP", "AUD", "NZD", "JPY", "CAD")
                .Where(b => b != a)
                .Select(b => (a, b)))
            .ToArbitrary();

    [Property(MaxTest = 500)]
    public Property Add_AcrossDistinctCurrencies_AlwaysThrows()
    {
        return Prop.ForAll(DistinctCurrencyPair(),
            pair =>
            {
                var (a, b) = pair;
                var moneyA = Money.Create(10m, a);
                var moneyB = Money.Create(5m, b);
                try
                { moneyA.Add(moneyB); return false; }
                catch (DomainRuleException) { return true; }
            });
    }

    [Property(MaxTest = 500)]
    public Property Subtract_AcrossDistinctCurrencies_AlwaysThrows()
    {
        return Prop.ForAll(DistinctCurrencyPair(),
            pair =>
            {
                var (a, b) = pair;
                var moneyA = Money.Create(10m, a);
                var moneyB = Money.Create(5m, b);
                try
                { moneyA.Subtract(moneyB); return false; }
                catch (DomainRuleException) { return true; }
            });
    }

    [Property(MaxTest = 500)]
    public Property IsValidCurrencyCode_RejectsNonLetterThreeCharCodes()
    {
        // Build 3-char codes from a hostile alphabet (digits, whitespace, symbols, unicode) so the
        // char-range validation loop in Money.IsValidCurrencyCode is genuinely exercised, not the
        // fixed ~11 ISO literals the other tests use. Codes containing any non A-Za-z char are invalid.
        var hostileChar = Gen.Elements('0', '9', ' ', '\t', '$', '!', '-', '€', 'Ω', 'é', '中');
        var threeCharCode = Gen.ArrayOf(hostileChar, 3).Select(chars => new string(chars)).ToArbitrary();
        return Prop.ForAll(threeCharCode,
            code =>
            {
                var allLetters = code.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'));
                var isValid = Money.IsValidCurrencyCode(code);
                // Hostile alphabet guarantees at least one non-letter in practice, but assert the exact
                // contract: valid iff all three chars are ASCII letters.
                return (isValid == allLetters).Label($"code='{code}' valid={isValid} expectedAllLetters={allLetters}");
            });
    }
}
