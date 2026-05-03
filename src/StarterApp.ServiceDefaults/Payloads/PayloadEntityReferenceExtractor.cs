using System.Text.Json;

namespace StarterApp.ServiceDefaults.Payloads;

public static class PayloadEntityReferenceExtractor
{
    private static readonly HashSet<string> IgnoredIdentifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "connectionId",
        "correlationId",
        "messageId",
        "operationId",
        "parentId",
        "requestId",
        "spanId",
        "traceId"
    };

    public static IReadOnlyList<PayloadEntityReference> Extract(PayloadCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var references = new Dictionary<string, PayloadEntityReference>(StringComparer.OrdinalIgnoreCase);
        var contextEntityType = InferRootEntityType(request.Operation, request.Metadata);

        foreach (var reference in request.EntityReferences)
            AddReference(reference.EntityType, reference.EntityId, references);

        AddRouteReference(request.Operation, request.Metadata, references);

        foreach (var metadata in request.Metadata)
            AddReferenceFromName(metadata.Key, metadata.Value, contextEntityType, references);

        AddJsonReferences(request.Payload, request.ContentType, contextEntityType, references);

        return references.Values
            .OrderBy(reference => reference.EntityType, StringComparer.Ordinal)
            .ThenBy(reference => reference.EntityId, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddJsonReferences(
        string payload,
        string? contentType,
        string? contextEntityType,
        Dictionary<string, PayloadEntityReference> references)
    {
        if (string.IsNullOrWhiteSpace(payload) || !LooksLikeJson(payload, contentType))
            return;

        try
        {
            using var document = JsonDocument.Parse(payload);
            AddJsonElementReferences(document.RootElement, contextEntityType, references);
        }
        catch (JsonException)
        {
            // Payload capture should not fail because one body was not valid JSON.
        }
    }

    private static void AddJsonElementReferences(
        JsonElement element,
        string? contextEntityType,
        Dictionary<string, PayloadEntityReference> references)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddReferenceFromJsonProperty(property.Name, property.Value, contextEntityType, references);
                    AddJsonElementReferences(property.Value, contextEntityType, references);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    AddJsonElementReferences(item, contextEntityType, references);
                break;
        }
    }

    private static void AddReferenceFromJsonProperty(
        string propertyName,
        JsonElement value,
        string? contextEntityType,
        Dictionary<string, PayloadEntityReference> references)
    {
        if (!TryGetScalarValue(value, out var scalarValue))
            return;

        AddReferenceFromName(propertyName, scalarValue, contextEntityType, references);
    }

    private static void AddReferenceFromName(
        string name,
        string value,
        string? contextEntityType,
        Dictionary<string, PayloadEntityReference> references)
    {
        if (IgnoredIdentifierNames.Contains(name))
            return;

        string? entityType = null;

        if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
        {
            entityType = contextEntityType;
        }
        else if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            entityType = name[..^2];
        }

        if (string.IsNullOrWhiteSpace(entityType))
            return;

        AddReference(entityType, value, references);
    }

    private static void AddRouteReference(
        string operation,
        IReadOnlyDictionary<string, string> metadata,
        Dictionary<string, PayloadEntityReference> references)
    {
        var pathSegments = SplitPath(ResolvePath(operation, metadata));

        for (var index = 0; index < pathSegments.Count - 1; index++)
        {
            var entitySegment = pathSegments[index];
            var idSegment = pathSegments[index + 1];
            if (!IsEntitySegment(entitySegment) || !IsLikelyIdentifier(idSegment))
                continue;

            AddReference(entitySegment, idSegment, references);
        }
    }

    private static string? InferRootEntityType(string operation, IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var segment in SplitPath(ResolvePath(operation, metadata)))
        {
            if (IsEntitySegment(segment))
                return NormalizeEntityType(segment);
        }

        return null;
    }

    private static string ResolvePath(string operation, IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.TryGetValue("path", out var metadataPath) && !string.IsNullOrWhiteSpace(metadataPath))
            return metadataPath;

        var operationParts = operation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return operationParts.Length > 1 ? operationParts[1] : operation;
    }

    private static List<string> SplitPath(string path)
    {
        var pathOnly = path.Split('?', 2, StringSplitOptions.TrimEntries)[0];
        return pathOnly.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool LooksLikeJson(string payload, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return true;

        var trimmed = payload.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static bool TryGetScalarValue(JsonElement value, out string scalarValue)
    {
        scalarValue = string.Empty;

        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                scalarValue = value.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(scalarValue);

            case JsonValueKind.Number:
                scalarValue = value.GetRawText();
                return true;

            default:
                return false;
        }
    }

    private static bool IsEntitySegment(string segment)
    {
        return !string.IsNullOrWhiteSpace(segment) &&
            !string.Equals(segment, "api", StringComparison.OrdinalIgnoreCase) &&
            !IsVersionSegment(segment) &&
            !IsLikelyIdentifier(segment) &&
            !segment.Contains('{', StringComparison.Ordinal);
    }

    private static bool IsVersionSegment(string segment)
    {
        if (segment.Length < 2 || segment[0] is not ('v' or 'V'))
            return false;

        return segment[1..].All(char.IsDigit);
    }

    private static bool IsLikelyIdentifier(string segment)
    {
        return !string.IsNullOrWhiteSpace(segment) &&
            !segment.Contains('{', StringComparison.Ordinal) &&
            segment.Any(char.IsDigit);
    }

    private static void AddReference(
        string entityType,
        string entityId,
        Dictionary<string, PayloadEntityReference> references)
    {
        var normalizedEntityType = NormalizeEntityType(entityType);
        var normalizedEntityId = NormalizeIdentifier(entityId);
        if (string.IsNullOrWhiteSpace(normalizedEntityType) || string.IsNullOrWhiteSpace(normalizedEntityId))
            return;

        var key = $"{normalizedEntityType}:{normalizedEntityId}";
        references.TryAdd(key, new PayloadEntityReference(normalizedEntityType, normalizedEntityId));
    }

    private static string NormalizeEntityType(string value)
    {
        var normalized = NormalizeIdentifier(value).ToLowerInvariant();
        if (normalized.EndsWith("ies", StringComparison.Ordinal) && normalized.Length > 3)
            return normalized[..^3] + "y";

        if (normalized.EndsWith('s') && normalized.Length > 1)
            return normalized[..^1];

        return normalized;
    }

    private static string NormalizeIdentifier(string value)
    {
        var chars = value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray();

        return chars.Length == 0 ? string.Empty : new string(chars);
    }
}
