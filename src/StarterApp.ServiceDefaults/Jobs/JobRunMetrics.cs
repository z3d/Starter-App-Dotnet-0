using System.Diagnostics.Metrics;

namespace StarterApp.ServiceDefaults.Jobs;

public static class JobRunMetrics
{
    public const string MeterName = "StarterApp.Jobs";
    public const string RunsInstrument = "starterapp.scheduled_job.runs";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>(RunsInstrument, description: "Scheduled job runs, by job and outcome");

    public static void Record(string job, string outcome)
        => Runs.Add(1, new KeyValuePair<string, object?>("job", job), new KeyValuePair<string, object?>("outcome", outcome));
}
