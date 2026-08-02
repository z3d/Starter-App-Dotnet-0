using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Fuzzing;

// Property-based coverage of CorrelationContext.Sanitize, the only remaining gate on the
// caller-controlled correlation id (the retired gateway header parser used to reject
// out-of-contract ids at the door). Its output feeds archive blob names (PayloadBlobNaming)
// and the echoed X-Correlation-ID response header, so the contract is load-bearing:
// every output matches [A-Za-z0-9._-]{1,128}, and distinct raw ids do not accidentally
// collapse onto one sanitized id (the raw-bound hash suffix carries that guarantee against
// benign collisions only — a caller can always construct a contract-valid id equal to a lossy
// raw's output, but the id is unauthenticated input, so the suffix was never an adversarial
// boundary).
public class CorrelationIdFuzzTests
{
    private const string ContractPattern = "^[A-Za-z0-9._-]{1,128}$";

    // Raw ids the way hostile or sloppy callers actually send them: contract-valid tokens,
    // ASCII noise (separators, whitespace, control chars), non-ASCII letters/digits that
    // char.IsLetterOrDigit would have admitted, surrogate pairs, and overlong runs.
    private static Gen<string> RawIdGen()
    {
        var contractValid = Gen.Choose(1, 40).SelectMany(length =>
            Gen.ArrayOf(Gen.Elements("abcXYZ0123456789-_.".ToCharArray()), length)
               .Select(cs => new string(cs)));

        var asciiNoise = Gen.Choose(0, 60).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(i => (char)i), length)
               .Select(cs => new string(cs)));

        var unicode = Gen.Elements(
            "città", "трейс-123", "标识符", "Ωμέγα", "٣٢١", "🔥trace🔥", "trace:🙂:abc",
            "éclair", "​zero-width", "ｆｕｌｌｗｉｄｔｈ１２３");

        var overlong = Gen.Choose(129, 400).SelectMany(length =>
            Gen.Elements('a', 'ü', ':', '7').Select(c => new string(c, length)));

        var whitespace = Gen.Elements("", " ", "\t\t", "  spaced value  ", "\r\n");

        return Gen.Frequency(
            (3, contractValid),
            (3, asciiNoise),
            (3, unicode),
            (1, overlong),
            (1, whitespace));
    }

    private static Arbitrary<string> RawIdArb() => RawIdGen().ToArbitrary();

    [Property(MaxTest = 400)]
    public Property Sanitize_AlwaysProducesAContractValidId()
    {
        return Prop.ForAll(RawIdArb(), raw =>
            System.Text.RegularExpressions.Regex.IsMatch(CorrelationContext.Sanitize(raw), ContractPattern));
    }

    [Property(MaxTest = 200)]
    public Property Sanitize_IsDeterministic_ForAnyNonEmptyInput()
    {
        // Every non-empty trimmed input must sanitize stably — including ids whose characters are
        // all stripped, which bind to the raw value as "invalid.<hash>". Only empty/whitespace
        // input (no caller-supplied id at all) gets a freshly generated one.
        return Prop.ForAll(RawIdArb(), raw =>
            raw.Trim().Length == 0 || CorrelationContext.Sanitize(raw) == CorrelationContext.Sanitize(raw));
    }

    [Property(MaxTest = 200)]
    public Property Sanitize_ContractValidInput_RoundTripsUnchanged()
    {
        var contractValid = Gen.Choose(1, 128).SelectMany(length =>
                Gen.ArrayOf(Gen.Elements("abcXYZ0123456789-_.".ToCharArray()), length)
                   .Select(cs => new string(cs)))
            .ToArbitrary();

        return Prop.ForAll(contractValid, raw =>
            CorrelationContext.Sanitize(raw) == raw);
    }

    [Property(MaxTest = 400)]
    public Property Sanitize_DistinctRawIds_DoNotAccidentallyCollapse()
    {
        // The raw-bound hash suffix keeps ids that sanitize to the same base ("a:b" vs "a|b",
        // "cliché" vs "cliche") out of each other's archive streams. This is a benign-collision
        // guarantee, not an adversarial one: a contract-valid raw constructed to equal a lossy
        // raw's exact output ("abcdef.<its hash>") round-trips verbatim and collides — but the
        // generator only produces such a pair by guessing an 8-hex hash, and a caller who wants
        // into another stream can just send that stream's id directly.
        return Prop.ForAll(RawIdArb(), RawIdArb(), (first, second) =>
            (first.Trim() == second.Trim() ||
             CorrelationContext.Sanitize(first) != CorrelationContext.Sanitize(second))
            .Label($"'{first}' and '{second}' collapsed onto one sanitized id"));
    }
}
