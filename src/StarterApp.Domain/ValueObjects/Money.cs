namespace StarterApp.Domain.ValueObjects;

public sealed record Money
{
    public const int MaxCurrencyLength = 3;
    public const int CurrencyDecimalPlaces = 2;

    // Above this the numeric(18,2) column overflows (SqlState 22003) and a client request turns into a 500. Validators check against it first.
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency = "USD")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, MaxAmount);

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (!IsValidCurrencyCode(currency))
            throw new ArgumentException("Currency code must be a three-letter ISO code", nameof(currency));

        // Rounds to whole cents, away from zero, the way Australian GST is rounded.
        var rounded = decimal.Round(amount, CurrencyDecimalPlaces, MidpointRounding.AwayFromZero);
        return new Money(rounded, currency.ToUpperInvariant());
    }

    public static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: MaxCurrencyLength } && currency.All(char.IsAsciiLetter);

    public static Money FromDecimal(decimal amount)
    {
        return Create(amount);
    }

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Currency != Currency)
            throw new DomainRuleException("Cannot add money with different currencies");

        // Goes through Create() so MaxAmount is checked. Add used to skip that check.
        return Create(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Currency != Currency)
            throw new DomainRuleException("Cannot subtract money with different currencies");

        return Create(Amount - other.Amount, Currency);
    }

    public override string ToString()
    {
        return $"{Amount} {Currency}";
    }
}
