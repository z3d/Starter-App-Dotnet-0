using System.Globalization;

namespace StarterApp.ServiceDefaults.Payloads;

public static class PayloadBlobNaming
{
    public static string BuildArchiveBlobName(DateTimeOffset timestampUtc, string correlationId, string archivePrefix = "archive")
    {
        var minutePath = BuildMinutePath(timestampUtc);
        return $"{TrimPrefix(archivePrefix)}/{minutePath}/{CorrelationContext.Sanitize(correlationId)}.jsonl";
    }

    public static string BuildAuditBlobName(DateTimeOffset timestampUtc, string auditPrefix = "audit")
    {
        var minutePath = BuildMinutePath(timestampUtc);
        return $"{TrimPrefix(auditPrefix)}/{minutePath}/payload-audit.jsonl";
    }

    public static string BuildEntityIndexBlobName(
        DateTimeOffset timestampUtc,
        PayloadEntityReference entityReference,
        string correlationId,
        string entityIndexPrefix = "entity-index")
    {
        ArgumentNullException.ThrowIfNull(entityReference);

        var minutePath = BuildMinutePath(timestampUtc);
        var entityType = SanitizePathPart(entityReference.EntityType, "unknown-entity");
        var entityId = SanitizePathPart(entityReference.EntityId, "unknown-id");
        return $"{TrimPrefix(entityIndexPrefix)}/{entityType}/{entityId}/{minutePath}/{CorrelationContext.Sanitize(correlationId)}.jsonl";
    }

    public static bool TryGetBlobMinute(string blobName, out DateTimeOffset minuteUtc)
    {
        minuteUtc = default;
        var parts = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return false;

        for (var index = 0; index <= parts.Length - 3; index++)
        {
            if (!DateOnly.TryParseExact(parts[index], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            if (!int.TryParse(parts[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
                !int.TryParse(parts[index + 2], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
                continue;

            if (hour is < 0 or > 23 || minute is < 0 or > 59)
                continue;

            minuteUtc = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);
            return true;
        }

        return false;
    }

    private static string BuildMinutePath(DateTimeOffset timestampUtc)
    {
        var utc = timestampUtc.ToUniversalTime();
        return $"{utc:yyyy-MM-dd}/{utc:HH}/{utc:mm}";
    }

    private static string TrimPrefix(string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix) ? "payloads" : prefix.Trim().Trim('/');
    }

    private static string SanitizePathPart(string value, string fallback)
    {
        var chars = value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray();

        return chars.Length == 0 ? fallback : new string(chars);
    }
}
