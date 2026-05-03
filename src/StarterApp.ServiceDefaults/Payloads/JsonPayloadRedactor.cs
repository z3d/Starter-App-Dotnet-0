using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StarterApp.ServiceDefaults.Payloads;

public sealed partial class JsonPayloadRedactor : IPayloadRedactor
{
    private const string Mask = "***REDACTED***";
    private readonly HashSet<string> _sensitiveNames;

    public JsonPayloadRedactor(IOptions<PayloadCaptureOptions> options)
    {
        _sensitiveNames = options.Value.SensitivePropertyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeName)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Redact(string payload, string? contentType = null)
    {
        if (string.IsNullOrEmpty(payload))
            return payload;

        if (IsJsonContent(contentType) || LooksLikeJson(payload))
        {
            try
            {
                var node = JsonNode.Parse(payload);
                if (node != null)
                {
                    RedactNode(node);
                    return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                }
            }
            catch (JsonException)
            {
                // Fall back to text masking below.
            }
        }

        return RedactText(payload);
    }

    private void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (IsSensitivePropertyName(property.Key))
                {
                    jsonObject[property.Key] = Mask;
                    continue;
                }

                if (property.Value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value))
                    jsonObject[property.Key] = RedactText(value);
                else if (property.Value != null)
                    RedactNode(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                var item = jsonArray[index];
                if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value))
                    jsonArray[index] = RedactText(value);
                else if (item != null)
                    RedactNode(item);
            }
        }
    }

    private bool IsSensitivePropertyName(string propertyName)
    {
        var normalizedPropertyName = NormalizeName(propertyName);
        return _sensitiveNames.Any(sensitiveName =>
            normalizedPropertyName.Equals(sensitiveName, StringComparison.OrdinalIgnoreCase) ||
            normalizedPropertyName.Contains(sensitiveName, StringComparison.OrdinalIgnoreCase));
    }

    private static string RedactText(string value)
    {
        return EmailRegex().Replace(value, Mask);
    }

    private static string NormalizeName(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static bool IsJsonContent(string? contentType)
    {
        return contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool LooksLikeJson(string payload)
    {
        var trimmed = payload.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
