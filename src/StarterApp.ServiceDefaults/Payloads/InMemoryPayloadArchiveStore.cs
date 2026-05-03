namespace StarterApp.ServiceDefaults.Payloads;

public sealed class InMemoryPayloadArchiveStore : IPayloadArchiveStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<string>> _lines = [];

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

    public Task<PayloadArchiveDeleteResult> DeleteOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var archiveDeleted = 0;
            var auditDeleted = 0;

            foreach (var blobName in _lines.Keys.ToList())
            {
                if (!PayloadBlobNaming.TryGetBlobMinute(blobName, out var blobMinuteUtc) || blobMinuteUtc >= cutoffUtc)
                    continue;

                _lines.Remove(blobName);
                if (blobName.StartsWith("archive/", StringComparison.Ordinal))
                    archiveDeleted++;
                else if (blobName.StartsWith("audit/", StringComparison.Ordinal))
                    auditDeleted++;
            }

            return Task.FromResult(new PayloadArchiveDeleteResult(archiveDeleted, auditDeleted));
        }
    }
}
