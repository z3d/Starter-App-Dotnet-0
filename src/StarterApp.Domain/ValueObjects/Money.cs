namespace StarterApp.Domain.ValueObjects;

public sealed record Money
{
    public const int MaxCurrencyLength = 3;
    public const int CurrencyDecimalPlaces = 2;

    // Persistence stores amounts as numeric(18,2); values past this overflow PostgreSQL
    // (SqlState 22003) and surface as a client-input-driven 500. Validators reference this
    // constant per the Validator–Domain Guard Sync Rule.
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

        // Money is always whole minor units (cents): quantize to 2 dp so computed values such as
        // GST and line/order totals never carry sub-cent precision into DTOs or domain events.
        // AwayFromZero (round-half-up) matches common tax rounding (e.g. Australian GST).
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

        // Through Create(), never the private constructor: it is the single guard that keeps every
        // Money within MaxAmount (numeric(18,2)), and Add was the one construction path around it.
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
