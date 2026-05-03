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

        return EmailRegex().Replace(payload, Mask);
    }

    private void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                if (_sensitiveNames.Contains(property.Key))
                    jsonObject[property.Key] = Mask;
                else if (property.Value != null)
                    RedactNode(property.Value);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                if (item != null)
                    RedactNode(item);
            }
        }
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
