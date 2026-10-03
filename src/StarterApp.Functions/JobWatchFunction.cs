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

public sealed record WatchedJob(string Name, string Schedule)
{
    public string? ScheduleSetting => Schedule is ['%', .., '%'] ? Schedule.Trim('%') : null;
}

public enum JobHealth
{
    Healthy,
    Failing,
    Overdue,
    Unwatchable,
}

public sealed class JobWatchOptions
{
    public const string SectionName = "JobWatch";

    public int OverdueGraceMinutes { get; set; } = 15;

    public int UnfinishedAfterMinutes { get; set; } = 120;
}

public sealed class JobWatchFunction
{
    private const string Stamp = "u";

    public static readonly IReadOnlyList<WatchedJob> Jobs = Discover(typeof(JobWatchFunction).Assembly);

    private readonly IJobRunHistory _history;
    private readonly IConfiguration _configuration;
    private readonly JobWatchOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobWatchFunction> _logger;

    public JobWatchFunction(IJobRunHistory history, IConfiguration configuration, IOptions<JobWatchOptions> options, TimeProvider timeProvider, ILogger<JobWatchFunction> logger)
    {
        _history = history;
        _configuration = configuration;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(JobWatchFunction))]
    public Task RunAsync([TimerTrigger("%JobWatch:Cron%")] TimerInfo timerInfo, CancellationToken cancellationToken)
        => WatchAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, JobHealth>> WatchAsync(CancellationToken cancellationToken)
        => WatchAsync(Jobs, cancellationToken);

    // The alert rules in the hosting repository match these log lines word for word (docs/runbooks/scheduled-jobs.md).
    public async Task<IReadOnlyDictionary<string, JobHealth>> WatchAsync(IReadOnlyList<WatchedJob> jobs, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var names = jobs.Select(job => job.Name).ToList();
        var lastRuns = await _history.LastRunsAsync(names, cancellationToken);
        if (lastRuns is null)
        {
            _logger.LogWarning("Job watch: there is no run history to watch without a database");
            return new Dictionary<string, JobHealth>();
        }

        var watchedSince = await _history.WatchedSinceAsync(names, now, cancellationToken);
        var grace = TimeSpan.FromMinutes(_options.OverdueGraceMinutes);
        var unfinishedAfter = TimeSpan.FromMinutes(_options.UnfinishedAfterMinutes);
        var health = new Dictionary<string, JobHealth>();
        foreach (var job in jobs)
        {
            var schedule = job.ScheduleSetting is { } setting ? _configuration[setting] : job.Schedule;
            if (string.IsNullOrWhiteSpace(schedule))
                continue;
            var last = lastRuns.GetValueOrDefault(job.Name);
            JobHealth state;
            DateTimeOffset? dueBy;
            try
            {
                (state, dueBy) = Judge(schedule, last, watchedSince.GetValueOrDefault(job.Name, now), now, grace, unfinishedAfter);
            }
            catch (FormatException)
            {
                health[job.Name] = JobHealth.Unwatchable;
                _logger.LogError("Scheduled job {Job} cannot be watched: its schedule '{Schedule}' is not a five- or six-field cron expression or an hh:mm:ss interval", job.Name, schedule);
                continue;
            }

            health[job.Name] = state;
            if (state == JobHealth.Overdue)
                _logger.LogError("Scheduled job {Job} is overdue: it last started {LastStarted} and should have run again by {DueBy}", job.Name, last?.StartedOnUtc.ToString(Stamp, CultureInfo.InvariantCulture) ?? "never", dueBy!.Value.ToString(Stamp, CultureInfo.InvariantCulture));
            else if (state == JobHealth.Failing && NeverFinished(last, now, unfinishedAfter))
                _logger.LogError("Scheduled job {Job} is failing: a run started {UnfinishedSince} never finished and none has finished since (it was killed or it hung)", job.Name, last!.UnfinishedSinceUtc!.Value.ToString(Stamp, CultureInfo.InvariantCulture));
            else if (state == JobHealth.Failing && last!.CompletedOutcome == JobOutcomes.Cancelled)
                _logger.LogError("Scheduled job {Job} is failing: its last run, started {LastStarted}, was cancelled before it finished", job.Name, last.StartedOnUtc.ToString(Stamp, CultureInfo.InvariantCulture));
            else if (state == JobHealth.Failing)
                _logger.LogError("Scheduled job {Job} is failing: its last run, started {LastStarted}, failed (the error is in job_runs)", job.Name, last!.StartedOnUtc.ToString(Stamp, CultureInfo.InvariantCulture));
        }

        _logger.LogInformation("Job watch: {Watched} scheduled jobs watched, {Unhealthy} need attention", health.Count, health.Count(pair => pair.Value != JobHealth.Healthy));
        return health;
    }

    public static (JobHealth Health, DateTimeOffset? DueBy) Judge(string schedule, LastRun? last, DateTimeOffset watchingSince, DateTimeOffset now, TimeSpan grace, TimeSpan unfinishedAfter)
    {
        var next = NextStart(schedule, last?.StartedOnUtc ?? watchingSince);
        if (next is { } due && due + grace <= now)
            return (JobHealth.Overdue, due + grace);
        var failing = NeverFinished(last, now, unfinishedAfter) || last?.CompletedOutcome is JobOutcomes.Failed or JobOutcomes.Cancelled;
        return (failing ? JobHealth.Failing : JobHealth.Healthy, null);
    }

    public static DateTimeOffset? NextStart(string schedule, DateTimeOffset from)
    {
        if (schedule.Contains(':', StringComparison.Ordinal))
        {
            if (TimeSpan.TryParse(schedule, CultureInfo.InvariantCulture, out var interval) && interval > TimeSpan.Zero)
                return from + interval;
            throw new FormatException($"'{schedule}' is not an interval");
        }

        var format = schedule.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length switch
        {
            6 => CronFormat.IncludeSeconds,
            5 => CronFormat.Standard,
            _ => throw new FormatException($"'{schedule}' is not a five- or six-field cron expression"),
        };
        return CronExpression.Parse(schedule, format).GetNextOccurrence(from, TimeZoneInfo.Utc);
    }

    public static IReadOnlyList<MethodInfo> TimerMethods(Assembly assembly)
        => assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => TimerOf(method) is not null)
            .ToList();

    public static IReadOnlyList<WatchedJob> Discover(Assembly assembly)
        => TimerMethods(assembly)
            .Select(method => (Watched: method.GetCustomAttribute<WatchedJobAttribute>(), Timer: TimerOf(method)!))
            .Where(pair => pair.Watched is not null)
            .Select(pair => new WatchedJob(pair.Watched!.Name, pair.Timer.Schedule))
            .OrderBy(job => job.Name, StringComparer.Ordinal)
            .ToList();

    private static TimerTriggerAttribute? TimerOf(MethodInfo method)
        => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<TimerTriggerAttribute>()).FirstOrDefault(timer => timer is not null);

    private static bool NeverFinished(LastRun? last, DateTimeOffset now, TimeSpan unfinishedAfter)
        => last?.UnfinishedSinceUtc is { } since && since + unfinishedAfter <= now;
}
