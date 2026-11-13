namespace StarterApp.ServiceDefaults.Jobs;

public sealed class NoJobRunHistory : IJobRunHistory
{
    public Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, LastRun>?>(null);

    public Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(new Dictionary<string, DateTimeOffset>());
}
