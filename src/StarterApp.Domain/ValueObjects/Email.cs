namespace StarterApp.Domain.ValueObjects;

public class Email : IEquatable<Email>
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

        // Normalize to a canonical lower-case form so the value object, the EF uniqueness query,
        // and the case-sensitive unique index all agree on identity. ToLowerInvariant (not ToLower)
        // is required: culture-sensitive casing would make the same address normalize differently
        // by locale (e.g. the Turkish dotless-i), so two hosts could disagree about collisions.
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

    public bool Equals(Email? other)
    {
        if (other is null)
            return false;

        return Value == other.Value;
    }

    public override bool Equals(object? obj) => Equals(obj as Email);

    public override int GetHashCode()
    {
        return Value.GetHashCode(StringComparison.Ordinal);
    }

    public static bool operator ==(Email? left, Email? right) => Equals(left, right);
    public static bool operator !=(Email? left, Email? right) => !Equals(left, right);

    public override string ToString()
    {
        return Value;
    }
}

