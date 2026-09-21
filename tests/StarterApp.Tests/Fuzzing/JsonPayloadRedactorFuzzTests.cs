using System.Text.Json.Nodes;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Payloads;

namespace StarterApp.Tests.Fuzzing;

public class JsonPayloadRedactorFuzzTests
{
    private const string Mask = "***REDACTED***";

    private static JsonPayloadRedactor CreateRedactor() =>
        new(Options.Create(new PayloadCaptureOptions()));

    // A small fixed set of configured-sensitive keys (subset of DefaultSensitivePropertyNames) and
    // some innocuous keys. The redactor matches sensitive names by normalized substring + case-insensitively,
    // so "userPassword" and "EMAIL_ADDRESS" are also sensitive.
    private static readonly string[] SensitiveKeys = ["password", "token", "ssn", "secret", "email"];
    private static readonly string[] InnocuousKeys = ["count", "id", "quantity", "status", "città"];

    private static Gen<string> KeyGen() =>
        Gen.OneOf(
            Gen.Elements(SensitiveKeys),
            // Sensitive substrings embedded in larger keys / mixed casing — must still redact.
            Gen.Elements("userPassword", "AccessToken", "EMAIL", "customerSecret", "taxFileNumber"),
            Gen.Elements(InnocuousKeys));

    // Recursively generate a JsonNode tree of bounded depth: objects, arrays, and scalar leaves.
    private static Gen<JsonNode?> NodeGen(int depth)
    {
        var scalar = Gen.OneOf(
            Gen.Choose(0, 1000).Select(i => (JsonNode?)JsonValue.Create(i)),
            Gen.Elements("hello", "user@example.com", "value", "", "δοκιμή").Select(s => (JsonNode?)JsonValue.Create(s)),
            Gen.Elements(true, false).Select(b => (JsonNode?)JsonValue.Create(b)));

        if (depth <= 0)
            return scalar;

        var objectGen = Gen.Choose(0, 4).SelectMany(count =>
            Gen.ArrayOf(
                KeyGen().SelectMany(key => NodeGen(depth - 1).Select(value => (Key: key, Value: value))),
                count)
            .Select(pairs =>
            {
                var obj = new JsonObject();
                foreach (var (key, value) in pairs)
                    obj[key] = value?.DeepClone();
                return (JsonNode?)obj;
            }));

        var arrayGen = Gen.Choose(0, 4).SelectMany(count =>
            Gen.ArrayOf(NodeGen(depth - 1), count)
            .Select(items =>
            {
                var arr = new JsonArray();
                foreach (var item in items)
                    arr.Add(item?.DeepClone());
                return (JsonNode?)arr;
            }));

        return Gen.Frequency(
            (3, scalar),
            (2, objectGen),
            (2, arrayGen));
    }

    private static Arbitrary<string> JsonPayloadArb() =>
        Gen.Choose(0, 3)
            .SelectMany(NodeGen)
            .Select(node => (node ?? new JsonObject()).ToJsonString())
            .ToArbitrary();

    private static bool IsSensitiveName(string name)
    {
        var normalized = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return SensitiveKeys.Any(s => normalized.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    // Walk the redacted tree and assert every sensitive-named property has been masked wholesale,
    // regardless of nesting depth or original value type.
    private static bool AllSensitivePropertiesMasked(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    if (IsSensitiveName(key))
                    {
                        if (value is not JsonValue v || !v.TryGetValue<string>(out var s) || s != Mask)
                            return false;
                    }
                    else if (!AllSensitivePropertiesMasked(value))
                    {
                        return false;
                    }
                }

                return true;
            case JsonArray arr:
                return arr.All(AllSensitivePropertiesMasked);
            default:
                return true;
        }
    }

    [Property(MaxTest = 500)]
    public Property RedactingValidJson_NeverThrows_AndProducesValidJson()
    {
        var redactor = CreateRedactor();
        return Prop.ForAll(JsonPayloadArb(),
            payload =>
            {
                string output;
                try
                {
                    output = redactor.Redact(payload, "application/json");
                }
                catch (Exception ex)
                {
                    return false.Label($"Redact threw {ex.GetType().Name} on payload: {payload}");
                }

                try
                {
                    JsonNode.Parse(output);
                    return true.ToProperty();
                }
                catch (Exception ex)
                {
                    return false.Label($"Output not valid JSON ({ex.GetType().Name}): {output}");
                }
            });
    }

    [Property(MaxTest = 500)]
    public Property AllConfiguredSensitiveProperties_AreRedacted_RegardlessOfNestingOrCasing()
    {
        var redactor = CreateRedactor();
        return Prop.ForAll(JsonPayloadArb(),
            payload =>
            {
                var output = redactor.Redact(payload, "application/json");
                var parsed = JsonNode.Parse(output);
                return AllSensitivePropertiesMasked(parsed)
                    .Label($"A sensitive property survived redaction.\nInput:  {payload}\nOutput: {output}");
            });
    }
}
