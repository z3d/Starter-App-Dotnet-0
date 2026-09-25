namespace StarterApp.Domain.ValueObjects;

public sealed record Email
{
    public const int MaxEmailLength = 320;

    public string Value { get; private set; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        // ToLowerInvariant, not ToLower: culture-sensitive casing (Turkish dotless i) would make hosts disagree about collisions.
        var normalized = value.ToLowerInvariant();

        if (normalized.Length > MaxEmailLength)
            throw new ArgumentException($"Email cannot exceed {MaxEmailLength} characters", nameof(value));

        if (!IsValidAddress(normalized))
            throw new ArgumentException("Invalid email format", nameof(value));

        return new Email(normalized);
    }

    public static bool IsValidAddress(string? email)
    {
        return !string.IsNullOrWhiteSpace(email)
            && email.Length <= MaxEmailLength
            && System.Net.Mail.MailAddress.TryCreate(email, out var addr)
            && addr.Address == email;
    }

    public override string ToString()
    {
        return Value;
    }
}

