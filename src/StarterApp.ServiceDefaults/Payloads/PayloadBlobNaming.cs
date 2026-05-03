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

    public static bool TryGetBlobMinute(string blobName, out DateTimeOffset minuteUtc)
    {
        minuteUtc = default;
        var parts = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
            return false;

        if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
            return false;

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return false;

        minuteUtc = new DateTimeOffset(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);
        return true;
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
}
