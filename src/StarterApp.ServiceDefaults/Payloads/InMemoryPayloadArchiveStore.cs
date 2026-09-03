namespace StarterApp.ServiceDefaults.Payloads;

public sealed class InMemoryPayloadArchiveStore : IPayloadArchiveStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<string>> _lines = [];
    private readonly PayloadCaptureOptions _options;

    public InMemoryPayloadArchiveStore(PayloadCaptureOptions? options = null)
    {
        _options = options ?? new PayloadCaptureOptions();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToList());
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lines.Clear();
        }
    }

    public Task AppendLineAsync(string blobName, string line, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_lines.TryGetValue(blobName, out var lines))
            {
                lines = [];
                _lines[blobName] = lines;
            }

            lines.Add(line);
        }

        return Task.CompletedTask;
    }

    // Cleanup mirrors AzureBlobPayloadArchiveStore semantics so the tests that run against this
    // double exercise the same contract: every expired blob under the three configured prefixes
    // is deleted in one pass, nothing outside them is touched. The double has no wall clock, so
    // the budget never runs out here.
    public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var archiveDeleted = DeletePrefixOlderThan(_options.ArchivePrefix, cutoffUtc);
            var auditDeleted = DeletePrefixOlderThan(_options.AuditPrefix, cutoffUtc);
            var entityIndexDeleted = DeletePrefixOlderThan(_options.EntityIndexPrefix, cutoffUtc);

            return Task.FromResult(new PayloadArchiveDeleteResult(archiveDeleted, auditDeleted, entityIndexDeleted));
        }
    }

    private int DeletePrefixOlderThan(string prefix, DateTimeOffset cutoffUtc)
    {
        var normalizedPrefix = prefix.Trim().Trim('/') + "/";
        var deleted = 0;

        foreach (var blobName in _lines.Keys
                     .Where(name => name.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                     .OrderBy(name => name, StringComparer.Ordinal)
                     .ToList())
        {
            if (!PayloadBlobNaming.TryGetBlobMinute(blobName, out var blobMinuteUtc) || blobMinuteUtc >= cutoffUtc)
                continue;

            _lines.Remove(blobName);
            deleted++;
        }

        return deleted;
    }
}
