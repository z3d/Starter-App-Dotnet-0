using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Fuzzing;

// Property-based coverage of the correlation-id contract enforced by GatewayIdentityHeaders.Read:
// [A-Za-z0-9._-]{1,128}. In-charset/in-length ids are accepted; out-of-charset, over-128, or empty
// ids cause the read to fail with an error naming the correlation header. GatewayIdentityHeaders is
// internal but reachable via InternalsVisibleTo("StarterApp.Tests").
public class CorrelationIdFuzzTests
{
    private static DefaultHttpContext ContextWithCorrelationId(string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[GatewayIdentityHeaders.Subject] = "user-123";
        context.Request.Headers[GatewayIdentityHeaders.PrincipalType] = AuthenticatedPrincipalType.User.ToString();
        context.Request.Headers[GatewayIdentityHeaders.TenantId] = "tenant-123";
        context.Request.Headers[GatewayIdentityHeaders.Scopes] = "orders:read products:write";
        context.Request.Headers[GatewayIdentityHeaders.AuthenticationMethods] = "mfa pwd";
        context.Request.Headers[CorrelationContext.HeaderName] = correlationId;
        return context;
    }

    private static bool ReadFailsForCorrelationId(IHeaderDictionary headers)
    {
        var result = GatewayIdentityHeaders.Read(headers);
        return !result.Succeeded
            && result.Errors.Any(error => error.Contains(CorrelationContext.HeaderName, StringComparison.Ordinal));
    }

    private static Gen<char> InCharsetChar() =>
        Gen.OneOf(
            Gen.Choose('A', 'Z').Select(i => (char)i),
            Gen.Choose('a', 'z').Select(i => (char)i),
            Gen.Choose('0', '9').Select(i => (char)i),
            Gen.Elements('.', '_', '-'));

    [Property(MaxTest = 500)]
    public Property InCharsetCorrelationId_WithinLength_IsAccepted()
    {
        var arb = Gen.Choose(1, 128)
            .SelectMany(len => Gen.ArrayOf(InCharsetChar(), len))
            .Select(chars => new string(chars))
            .ToArbitrary();

        return Prop.ForAll(arb,
            correlationId =>
            {
                var result = GatewayIdentityHeaders.Read(ContextWithCorrelationId(correlationId).Request.Headers);
                return (result.Succeeded && result.Envelope!.User.CorrelationId == correlationId)
                    .Label($"In-charset id '{correlationId}' (len={correlationId.Length}) should be accepted verbatim.");
            });
    }

    [Property(MaxTest = 500)]
    public Property OverLengthCorrelationId_IsRejected()
    {
        // > 128 in-charset chars: charset is fine, length contract is violated.
        var arb = Gen.Choose(129, 1_024)
            .SelectMany(len => Gen.ArrayOf(InCharsetChar(), len))
            .Select(chars => new string(chars))
            .ToArbitrary();

        return Prop.ForAll(arb,
            correlationId =>
                ReadFailsForCorrelationId(ContextWithCorrelationId(correlationId).Request.Headers)
                    .Label($"Over-length id (len={correlationId.Length}) should be rejected."));
    }

    [Property(MaxTest = 500)]
    public Property OutOfCharsetCorrelationId_IsRejected()
    {
        // Inject at least one out-of-charset character into an otherwise valid id. Exclusions:
        //  - header-unsafe chars (CR/LF/comma) fail earlier with a different header-safety error;
        //  - whitespace is intentionally NOT here: ReadSingleHeader .Trim()s the value, so leading/
        //    trailing spaces are stripped before the charset check (documented behavior, not a violation).
        // Every char below is outside the correlation contract charset [A-Za-z0-9._-] and is non-whitespace,
        // so the resulting id must be rejected by the correlation-id charset check regardless of position.
        var hostileChar = Gen.Elements('+', '/', ':', '!', '@', '#', '%', '=', '中', 'é', '*', '(', ')');
        var arb = Gen.Choose(0, 10)
            .SelectMany(prefixLen => Gen.ArrayOf(InCharsetChar(), prefixLen)
                .SelectMany(prefix => hostileChar
                    .SelectMany(bad => Gen.Choose(0, 10)
                        .SelectMany(suffixLen => Gen.ArrayOf(InCharsetChar(), suffixLen)
                            .Select(suffix => new string(prefix) + bad + new string(suffix))))))
            .ToArbitrary();

        return Prop.ForAll(arb,
            correlationId =>
                ReadFailsForCorrelationId(ContextWithCorrelationId(correlationId).Request.Headers)
                    .Label($"Out-of-charset id '{correlationId}' should be rejected."));
    }

    [Fact]
    public void EmptyCorrelationId_IsRejectedAsMissing()
    {
        // The correlation header is read as a required header; empty/whitespace fails the read.
        var context = ContextWithCorrelationId(string.Empty);
        var result = GatewayIdentityHeaders.Read(context.Request.Headers);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains(CorrelationContext.HeaderName, StringComparison.Ordinal));
    }
}
