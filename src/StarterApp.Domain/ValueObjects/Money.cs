namespace StarterApp.Domain.ValueObjects;

public class Money : IEquatable<Money>
{
    public const int MaxCurrencyLength = 3;

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

        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (!IsValidCurrencyCode(currency))
            throw new ArgumentException("Currency code must be a three-letter ISO code", nameof(currency));

        return new Money(amount, currency.ToUpperInvariant());
    }

    public static bool IsValidCurrencyCode(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != MaxCurrencyLength)
            return false;

        foreach (var character in currency)
        {
            if (character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                return false;
        }

        return true;
    }

    public static Money FromDecimal(decimal amount)
    {
        return Create(amount);
    }

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
            throw new InvalidOperationException("Cannot add money with different currencies");

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        if (other.Currency != Currency)
            throw new InvalidOperationException("Cannot subtract money with different currencies");

        return Create(Amount - other.Amount, Currency);
    }

    public bool Equals(Money? other)
    {
        if (other is null)
            return false;

        return Amount == other.Amount && Currency == other.Currency;
    }

    public override bool Equals(object? obj) => Equals(obj as Money);

    public override int GetHashCode()
    {
        return HashCode.Combine(Amount, Currency);
    }

    public static bool operator ==(Money? left, Money? right) => Equals(left, right);
    public static bool operator !=(Money? left, Money? right) => !Equals(left, right);

    public override string ToString()
    {
        return $"{Amount} {Currency}";
    }
}
