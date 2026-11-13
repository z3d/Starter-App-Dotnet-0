namespace StarterApp.ServiceDefaults.Jobs;

public sealed class NullJobRunRecorder : IJobRunRecorder
{
    public Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken) => Task.FromResult(Guid.Empty);
    public Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken) => Task.CompletedTask;
}
