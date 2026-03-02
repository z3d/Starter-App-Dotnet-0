using FsCheck;
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
        return Prop.ForAll(Arb.Default.String(),
            (string? value) =>
            {
                try
                {
                    Email.Create(value!);
                    return true;
                }
                catch (ArgumentException) { return true; }
            });
    }
}
