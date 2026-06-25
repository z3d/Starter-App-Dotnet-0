using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace StarterApp.Tests.Fuzzing;

public class EmailFuzzTests
{
    [Property]
    public Property NullOrWhitespace_AlwaysRejected()
    {
        var whitespaceStrings = Gen.Elements("", " ", "  ", "\t", "\n")
            .ToArbitrary();
        return Prop.ForAll(whitespaceStrings,
            value =>
            {
                try
                { Email.Create(value); return false; }
                catch (ArgumentException) { return true; }
            });
    }

    [Property]
    public Property ValidEmails_AreAlwaysAccepted()
    {
        var validEmails = Gen.Elements(
            "test@example.com", "user@domain.org", "a@b.co",
            "user.name@company.com", "first.last@domain.io")
            .ToArbitrary();
        return Prop.ForAll(validEmails,
            email =>
            {
                var result = Email.Create(email);
                return result.Value == email;
            });
    }

    [Property]
    public Property Equality_IsReflexive()
    {
        var validEmails = Gen.Elements(
            "test@example.com", "user@domain.org", "a@b.co")
            .ToArbitrary();
        return Prop.ForAll(validEmails,
            email =>
            {
                var e1 = Email.Create(email);
                var e2 = Email.Create(email);
                return e1.Equals(e2);
            });
    }

    [Property]
    public Property RandomStrings_NeverCrashUnexpectedly()
    {
        return Prop.ForAll(ArbMap.Default.ArbFor<string>(),
            (string value) =>
            {
                try
                {
                    Email.Create(value);
                    return true;
                }
                catch (ArgumentException) { return true; }
            });
    }

    // ---- P1b: MaxEmailLength boundary (320) ----

    // Build an address of exactly the requested total length: "<local>@example.com" where the
    // suffix "@example.com" is 12 chars, so local-part length = total - 12.
    private static string AddressOfLength(int totalLength)
    {
        const string suffix = "@example.com";
        return new string('a', totalLength - suffix.Length) + suffix;
    }

    [Fact]
    public void EmailAtMaxLength_IsAccepted()
    {
        var email = AddressOfLength(Email.MaxEmailLength); // exactly 320 chars
        Assert.Equal(Email.MaxEmailLength, email.Length);
        var result = Email.Create(email);
        Assert.Equal(email, result.Value);
    }

    [Property(MaxTest = 500)]
    public Property EmailOverMaxLength_AlwaysRejected()
    {
        // Lengths strictly above 320 must be rejected with the documented ArgumentException.
        var overLength = Gen.Choose(Email.MaxEmailLength + 1, Email.MaxEmailLength + 2_000).ToArbitrary();
        return Prop.ForAll(overLength,
            length =>
            {
                var email = AddressOfLength(length);
                try
                { Email.Create(email); return false; }
                catch (ArgumentException) { return true; }
            });
    }

    // ---- P2b: hostile strings that parse via MailAddress but fail the addr.Address == email guard ----

    [Property(MaxTest = 500)]
    public Property DisplayNameAndBracketForms_AreRejected()
    {
        // MailAddress.TryCreate accepts display-name and angle-bracket forms, but Email.Create's
        // round-trip guard (addr.Address == normalized) rejects them because addr.Address strips the
        // display name / brackets, so the parsed address differs from the lower-cased input.
        var baseAddr = Gen.Elements("user@example.com", "a.b@domain.org", "test@host.io");
        var hostile = baseAddr.SelectMany(addr => Gen.Elements(
            $"Name <{addr}>",
            $"<{addr}>",
            $"\"Display\" <{addr}>",
            $"{addr}.",
            $" {addr} "))
            .ToArbitrary();
        return Prop.ForAll(hostile,
            value =>
            {
                try
                {
                    var created = Email.Create(value);
                    // If it is accepted at all (e.g. a trailing-dot form that MailAddress round-trips),
                    // the stored value must be the lower-cased input — anything else means the round-trip
                    // guard let a differing address through. Display-name/bracket forms throw below.
                    var ok = string.Equals(created.Value, value.ToLowerInvariant(), StringComparison.Ordinal);
                    return ok.Label($"Unexpectedly accepted hostile form '{value}' as '{created.Value}'");
                }
                catch (ArgumentException) { return true.ToProperty(); }
            });
    }

    [Property(MaxTest = 500)]
    public Property UnicodeAndControlChars_OnlyArgumentExceptionEscapes()
    {
        // Targeted hostile alphabet: unicode, IDN-ish, control chars, embedded nulls. The only
        // exception type allowed to escape Email.Create is ArgumentException — anything else is a bug.
        var hostileChar = Gen.Elements('\0', '', '​', '�', '中', 'é', '@', '.', ' ', 'x', '☃', '\n');
        var hostileString = Gen.Choose(1, 40)
            .SelectMany(len => Gen.ArrayOf(hostileChar, len))
            .Select(chars => new string(chars))
            .ToArbitrary();
        return Prop.ForAll(hostileString,
            value =>
            {
                try
                {
                    Email.Create(value);
                    return true;
                }
                catch (ArgumentException) { return true; }
            });
    }
}
