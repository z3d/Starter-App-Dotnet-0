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

        if (value.Length > MaxEmailLength)
            throw new ArgumentException($"Email cannot exceed {MaxEmailLength} characters", nameof(value));

        if (!IsValidEmail(value))
            throw new ArgumentException("Invalid email format", nameof(value));

        return new Email(value);
    }

    private static bool IsValidEmail(string email)
    {
        return System.Net.Mail.MailAddress.TryCreate(email, out var addr)
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
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value;
    }
}



