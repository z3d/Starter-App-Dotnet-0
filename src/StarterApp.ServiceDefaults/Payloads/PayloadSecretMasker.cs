using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StarterApp.ServiceDefaults.Payloads;

// Runs on every text payload before it is archived, audited or logged. Secrets have no audit value, so the archive's full-body rule stops short of them.
public static partial class PayloadSecretMasker
{
    public const string Mask = "***SECRET***";

    private static readonly string[] SecretNameParts =
    [
        "password", "passwd", "passphrase", "passcode", "secret", "token", "apikey", "privatekey",
        "onetimecode", "onetimepassword", "verificationcode", "cardnumber", "creditcard", "securitycode"
    ];

    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly HashSet<string> SecretNames = new(StringComparer.Ordinal)
    {
        "otp", "pin", "pan", "cvv", "cvv2", "cvc", "authorization"
    };

    public static bool IsSecretName(string name)
    {
        var normalized = Normalize(name);
        return SecretNames.Contains(normalized) || SecretNameParts.Any(part => normalized.Contains(part, StringComparison.Ordinal));
    }

    public static string MaskPayload(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return payload;

        if (TryMaskJson(payload, out var masked))
            return masked;

        return MaskText(payload);
    }

    // An unchanged body is returned as it came, so the archive keeps its bytes and hash.
    private static bool TryMaskJson(string text, out string masked)
    {
        masked = text;
        var trimmed = text.TrimStart();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
            return false;

        try
        {
            var node = JsonNode.Parse(text);
            if (node is null)
                return false;
            if (MaskNode(node))
                masked = node.ToJsonString(WriteOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static bool MaskNode(JsonNode node)
    {
        var changed = false;
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (IsSecretName(property.Key))
                {
                    jsonObject[property.Key] = Mask;
                    changed = true;
                }
                else if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var masked = MaskString(text);
                    if (masked != text)
                    {
                        jsonObject[property.Key] = masked;
                        changed = true;
                    }
                }
                else if (property.Value is not null)
                {
                    changed |= MaskNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                if (jsonArray[index] is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var masked = MaskString(text);
                    if (masked != text)
                    {
                        jsonArray[index] = masked;
                        changed = true;
                    }
                }
                else if (jsonArray[index] is { } item)
                {
                    changed |= MaskNode(item);
                }
            }
        }
        return changed;
    }

    private static string MaskString(string text) =>
        TryMaskJson(text, out var masked) ? masked : MaskText(text);

    private static string MaskText(string text)
    {
        var masked = SecretPairRegex().Replace(text, match =>
            IsSecretName(match.Groups["name"].Value) ? match.Groups["lead"].Value + Mask + match.Groups["trail"].Value : match.Value);
        return CardNumberRegex().Replace(masked, match => IsLuhnValid(match.Value) ? Mask : match.Value);
    }

    internal static bool IsLuhnValid(string candidate)
    {
        var digits = candidate.Where(char.IsAsciiDigit).Select(digit => digit - '0').ToArray();
        if (digits.Length is < 13 or > 19 || digits.All(digit => digit == 0))
            return false;

        var sum = 0;
        for (var index = 0; index < digits.Length; index++)
        {
            var digit = digits[digits.Length - 1 - index];
            if (index % 2 == 1)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // A "name": "value" pair in JSON that did not parse (a truncated body), or name=value in a form or query string.
    [GeneratedRegex("""(?<lead>"(?<name>[A-Za-z0-9_\-]{1,64})"\s*:\s*")(?:[^"\\]|\\.)*(?<trail>")|(?<lead>(?<=^|[&?\s])(?<name>[A-Za-z0-9_\-]{1,64})=)[^&\s]*(?<trail>)""", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPairRegex();

    // Contiguous digits, or groups of four split by single spaces or dashes; a digit-only GUID is never matched.
    [GeneratedRegex(@"(?<![\d-])(?:\d{13,19}|\d{4}(?:[ -]\d{4}){2}[ -]\d{1,7}|\d{4}[ -]\d{6}[ -]\d{5})(?![\d-])", RegexOptions.CultureInvariant)]
    private static partial Regex CardNumberRegex();
}
