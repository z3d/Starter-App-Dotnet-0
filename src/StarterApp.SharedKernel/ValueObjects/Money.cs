namespace StarterApp.SharedKernel.ValueObjects;

public sealed record Money
{
    public const int MaxCurrencyLength = 3;

    // Above this the numeric(18,2) column overflows (SqlState 22003) and a client request turns into a 500. Validators check against it first.
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private static readonly Dictionary<string, int> NonTwoPlaceMinorUnits =
        new[]
        {
            (0, "BIF CLP DJF GNF ISK JPY KMF KRW PYG RWF UGX UYI VND VUV XAF XOF XPF"),
            (3, "BHD IQD JOD KWD LYD OMR TND"),
            (4, "CLF UYW"),
        }
        .SelectMany(group => group.Item2.Split(' ').Select(code => (Code: code, Places: group.Item1)))
        .ToDictionary(entry => entry.Code, entry => entry.Places, StringComparer.Ordinal);

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string currency)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, MaxAmount);

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (!IsValidCurrencyCode(currency))
            throw new ArgumentException("Currency code must be a three-letter ISO code", nameof(currency));

        var code = currency.ToUpperInvariant();
        if (!IsAtScale(amount, code))
            throw new ArgumentException(
                $"{amount} has more decimal places than {code} allows ({MinorUnits(code)}); amounts are never rounded", nameof(amount));

        return new Money(amount, code);
    }

    public static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: MaxCurrencyLength } && currency.All(char.IsAsciiLetter);

    public static int MinorUnits(string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        return NonTwoPlaceMinorUnits.GetValueOrDefault(currency.ToUpperInvariant(), 2);
    }

    public static bool IsAtScale(decimal amount, string currency) =>
        decimal.Round(amount, MinorUnits(currency)) == amount;

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Currency != Currency)
            throw new DomainRuleException("Cannot add money with different currencies");

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
