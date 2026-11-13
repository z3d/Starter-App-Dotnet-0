namespace StarterApp.ServiceDefaults.Jobs;

public interface IJobRunHistory
{
    Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
