namespace StarterApp.ServiceDefaults.Jobs;

// A failure to record is logged and swallowed. Writing the history must never take down the job it describes.
public interface IJobRunRecorder
{
    Task<Guid> StartRunAsync(string jobName, DateTimeOffset startedOnUtc, CancellationToken cancellationToken);
    Task CompleteRunAsync(Guid runId, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken);
    Task RecordRunAsync(string jobName, DateTimeOffset startedOnUtc, DateTimeOffset completedOnUtc, string outcome, string summary, CancellationToken cancellationToken);
}
