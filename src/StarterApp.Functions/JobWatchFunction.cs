using System.Globalization;
using System.Reflection;
using Cronos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.ServiceDefaults.Jobs;

namespace StarterApp.Functions;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WatchedJobAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public sealed record WatchedJob(string Name, string CronSetting);

public enum JobHealth
{
    Healthy,
    Failing,
    Overdue,
}

public sealed class JobWatchOptions
{
    public const string SectionName = "JobWatch";

    public int OverdueGraceMinutes { get; set; } = 15;
}

public sealed class JobWatchFunction
{
    public static readonly IReadOnlyList<WatchedJob> Jobs = Discover(typeof(JobWatchFunction).Assembly);

    private readonly IJobRunHistory _history;
    private readonly IConfiguration _configuration;
    private readonly JobWatchOptions _options;
    private readonly JobWatchStart _start;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobWatchFunction> _logger;

    public JobWatchFunction(IJobRunHistory history, IConfiguration configuration, IOptions<JobWatchOptions> options, JobWatchStart start, TimeProvider timeProvider, ILogger<JobWatchFunction> logger)
    {
        _history = history;
        _configuration = configuration;
        _options = options.Value;
        _start = start;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(JobWatchFunction))]
    public Task RunAsync([TimerTrigger("%JobWatch:Cron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
        => WatchAsync(cancellationToken);

    // The alert rules in the hosting repository match these log lines word for word (docs/runbooks/scheduled-jobs.md).
    public async Task<IReadOnlyDictionary<string, JobHealth>> WatchAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var lastRuns = await _history.LastRunsAsync(Jobs.Select(job => job.Name).ToList(), cancellationToken);
        if (lastRuns is null)
        {
            _logger.LogWarning("Job watch: there is no run history to watch without a database");
            return new Dictionary<string, JobHealth>();
        }

        var grace = TimeSpan.FromMinutes(_options.OverdueGraceMinutes);
        var health = new Dictionary<string, JobHealth>();
        foreach (var job in Jobs)
        {
            var cron = _configuration[job.CronSetting];
            if (string.IsNullOrWhiteSpace(cron))
                continue;
            var last = lastRuns.GetValueOrDefault(job.Name);
            var (state, dueBy) = Judge(cron, last, _start.At, now, grace);
            health[job.Name] = state;
            if (state == JobHealth.Overdue)
                _logger.LogError("Scheduled job {Job} is overdue: it last started {LastStarted} and should have run again by {DueBy}", job.Name, last?.StartedOnUtc.ToString("u", CultureInfo.InvariantCulture) ?? "never", dueBy!.Value.ToString("u", CultureInfo.InvariantCulture));
            else if (state == JobHealth.Failing)
                _logger.LogError("Scheduled job {Job} is failing: its last run, started {LastStarted}, failed (the error is in job_runs)", job.Name, last!.StartedOnUtc.ToString("u", CultureInfo.InvariantCulture));
        }

        _logger.LogInformation("Job watch: {Watched} scheduled jobs watched, {Unhealthy} need attention", health.Count, health.Count(pair => pair.Value != JobHealth.Healthy));
        return health;
    }

    public static (JobHealth Health, DateTimeOffset? DueBy) Judge(string cron, LastRun? last, DateTimeOffset watchingSince, DateTimeOffset now, TimeSpan grace)
    {
        var next = CronExpression.Parse(cron, CronFormat.IncludeSeconds).GetNextOccurrence(last?.StartedOnUtc ?? watchingSince, TimeZoneInfo.Utc);
        if (next is { } due && due + grace <= now)
            return (JobHealth.Overdue, due + grace);
        return (last?.CompletedOutcome == "Failed" ? JobHealth.Failing : JobHealth.Healthy, null);
    }

    public static IReadOnlyList<WatchedJob> Discover(Assembly assembly)
        => assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Select(method => (Watched: method.GetCustomAttribute<WatchedJobAttribute>(), Timer: method.GetParameters().Select(parameter => parameter.GetCustomAttribute<TimerTriggerAttribute>()).FirstOrDefault(timer => timer is not null)))
            .Where(pair => pair.Watched is not null && pair.Timer is not null)
            .Select(pair => new WatchedJob(pair.Watched!.Name, pair.Timer!.Schedule.Trim('%')))
            .OrderBy(job => job.Name, StringComparer.Ordinal)
            .ToList();
}

public sealed class JobWatchStart
{
    public JobWatchStart(TimeProvider timeProvider)
    {
        At = timeProvider.GetUtcNow();
    }

    public DateTimeOffset At { get; }
}
