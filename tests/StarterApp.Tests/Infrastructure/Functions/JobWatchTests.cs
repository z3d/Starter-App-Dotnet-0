using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StarterApp.Functions;

namespace StarterApp.Tests.Infrastructure.Functions;

public class JobWatchTests
{
    private const string Hourly = "0 0 * * * *";
    private const string SundayEvening = "0 0 21 * * 0";
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Unfinished = TimeSpan.FromMinutes(120);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LongAgo = Now.AddDays(-30);
    private static readonly DateTimeOffset OnTheHour = new(2026, 9, 28, 0, 0, 1, TimeSpan.Zero);

    [Fact]
    public void EveryTimerJobMarkedWatched_IsDiscoveredWithTheSettingItsTimerRunsOn()
        => Assert.Equal([new WatchedJob(PayloadArchiveCleanupFunction.JobName, "%PayloadCapture:CleanupCron%")], JobWatchFunction.Jobs);

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Degraded")]
    public void ARunThatDidNotFail_OnSchedule_IsHealthy(string outcome)
        => Assert.Equal((JobHealth.Healthy, null), JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour, outcome), LongAgo, Now, Grace, Unfinished));

    [Fact]
    public void ARunThatFailed_IsFailing_UntilARunSucceeds()
        => Assert.Equal((JobHealth.Failing, null), JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour, "Failed"), LongAgo, Now, Grace, Unfinished));

    [Fact]
    public void ARunStillGoing_IsJudgedOnTheLastOneThatFinished_UntilItHasGoneOnTooLong()
    {
        Assert.Equal(JobHealth.Failing, JobWatchFunction.Judge(Hourly, new LastRun(Now.AddSeconds(-5), "Failed", Now.AddSeconds(-5)), LongAgo, Now, Grace, Unfinished).Health);
        Assert.Equal(JobHealth.Healthy, JobWatchFunction.Judge(Hourly, new LastRun(Now.AddSeconds(-5), "Succeeded", Now.AddMinutes(-119)), LongAgo, Now, Grace, Unfinished).Health);
        Assert.Equal(JobHealth.Failing, JobWatchFunction.Judge(Hourly, new LastRun(Now.AddSeconds(-5), "Succeeded", Now.AddMinutes(-120)), LongAgo, Now, Grace, Unfinished).Health);
    }

    [Fact]
    public void AJobKilledOnEveryRun_IsFailing_ThoughNoRunEverFinished()
        => Assert.Equal((JobHealth.Failing, null), JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour, null, OnTheHour.AddHours(-5)), LongAgo, Now, Grace, Unfinished));

    [Fact]
    public void ARunThatWasCancelled_IsFailing_UntilARunSucceeds()
        => Assert.Equal((JobHealth.Failing, null), JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour, "Cancelled"), LongAgo, Now, Grace, Unfinished));

    [Theory]
    [InlineData("0 0 * * * *", "2026-09-28T01:00:00Z")]
    [InlineData("0 * * * *", "2026-09-28T01:00:00Z")]
    [InlineData("00:45:00", "2026-09-28T01:15:00Z")]
    public void ASchedule_IsASixFieldCron_AFiveFieldCron_OrAnInterval(string schedule, string next)
        => Assert.Equal(DateTimeOffset.Parse(next, CultureInfo.InvariantCulture), JobWatchFunction.NextStart(schedule, Now));

    [Theory]
    [InlineData("every hour")]
    [InlineData("0 0 * * * * *")]
    [InlineData("61 * * * *")]
    [InlineData("00:00:00")]
    [InlineData("soon:ish")]
    public void AScheduleThatIsNeither_IsAFormatError(string schedule)
        => Assert.ThrowsAny<FormatException>(() => JobWatchFunction.NextStart(schedule, Now));

    [Fact]
    public void ATimerJob_IsFoundWhetherItsMethodIsStaticOrNot_AndItsScheduleALiteralOrASetting()
    {
        var timers = JobWatchFunction.TimerMethods(typeof(JobWatchTests).Assembly).Select(method => method.Name).Order(StringComparer.Ordinal);
        var watched = JobWatchFunction.Discover(typeof(JobWatchTests).Assembly);

        Assert.Equal([nameof(SyntheticTimers.Literal), nameof(SyntheticTimers.Setting), nameof(SyntheticTimers.Unwatched)], timers);
        Assert.Equal([new WatchedJob("synthetic-literal", "0 0 * * * *"), new WatchedJob("synthetic-setting", "%Synthetic:Cron%")], watched);
    }

    [Fact]
    public void AJobThatMissedItsNextRunByMoreThanTheGrace_IsOverdue_AndSaysWhenItWasDue()
    {
        var (health, dueBy) = JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour.AddHours(-2), "Succeeded"), LongAgo, Now, Grace, Unfinished);

        Assert.Equal(JobHealth.Overdue, health);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 23, 15, 0, TimeSpan.Zero), dueBy);
    }

    [Fact]
    public void AMissedRunWithinTheGrace_IsNotYetOverdue()
        => Assert.Equal(JobHealth.Healthy, JobWatchFunction.Judge(Hourly, new LastRun(OnTheHour.AddHours(-1), "Succeeded"), LongAgo, new DateTimeOffset(2026, 9, 28, 0, 14, 59, TimeSpan.Zero), Grace, Unfinished).Health);

    [Fact]
    public void AWeeklyJob_IsNotOverdueMidWeek_ButIsOnceItsRunIsMissed()
    {
        var lastSunday = new DateTimeOffset(2026, 9, 20, 21, 0, 2, TimeSpan.Zero);

        Assert.Equal(JobHealth.Healthy, JobWatchFunction.Judge(SundayEvening, new LastRun(lastSunday, "Succeeded"), LongAgo, new DateTimeOffset(2026, 9, 25, 3, 0, 0, TimeSpan.Zero), Grace, Unfinished).Health);
        Assert.Equal(JobHealth.Overdue, JobWatchFunction.Judge(SundayEvening, new LastRun(lastSunday, "Succeeded"), LongAgo, new DateTimeOffset(2026, 9, 27, 21, 15, 0, TimeSpan.Zero), Grace, Unfinished).Health);
    }

    [Fact]
    public void AnOverdueJob_IsOverdue_WhateverItsLastRunDid()
        => Assert.Equal(JobHealth.Overdue, JobWatchFunction.Judge(Hourly, new LastRun(Now.AddHours(-3), "Failed"), LongAgo, Now, Grace, Unfinished).Health);

    [Fact]
    public void AJobThatNeverRan_IsJudgedFromWhenTheWatchStarted()
    {
        Assert.Equal(JobHealth.Healthy, JobWatchFunction.Judge(Hourly, null, Now.AddMinutes(-10), Now, Grace, Unfinished).Health);
        Assert.Equal((JobHealth.Overdue, new DateTimeOffset(2026, 9, 28, 0, 15, 0, TimeSpan.Zero)), JobWatchFunction.Judge(Hourly, null, Now.AddMinutes(-80), Now, Grace, Unfinished));
    }

    [Fact]
    public async Task AFailedJob_IsOneErrorLine_AndThePassEndsWithTheWatchsOwnLine()
    {
        var logger = new ListLogger();

        var health = await Watch(Runs(new LastRun(OnTheHour, "Failed")), logger).WatchAsync(CancellationToken.None);

        Assert.Equal(JobHealth.Failing, health[PayloadArchiveCleanupFunction.JobName]);
        Assert.Equal(
        [
            (LogLevel.Error, "Scheduled job payload-archive-cleanup is failing: its last run, started 2026-09-28 00:00:01Z, failed (the error is in job_runs)"),
            (LogLevel.Information, "Job watch: 1 scheduled jobs watched, 1 need attention"),
        ], logger.Lines);
    }

    [Fact]
    public async Task AnOverdueJob_IsOneErrorLine_GivenItsCron()
    {
        var logger = new ListLogger();

        await Watch(Runs(new LastRun(OnTheHour.AddHours(-2), "Succeeded")), logger).WatchAsync(CancellationToken.None);

        Assert.Equal("Scheduled job payload-archive-cleanup is overdue: it last started 2026-09-27 22:00:01Z and should have run again by 2026-09-27 23:15:00Z", Assert.Single(logger.Lines, line => line.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task AJobThatNeverRan_IsOverdueOnceTheWatchHasWaitedForItsFirstRun()
    {
        var logger = new ListLogger();

        await Watch(Runs(), logger).WatchAsync(CancellationToken.None);

        Assert.Equal("Scheduled job payload-archive-cleanup is overdue: it last started never and should have run again by 2026-08-29 01:15:00Z", Assert.Single(logger.Lines, line => line.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task AJobThatNeverRan_IsJudgedFromWhenItWasFirstWatched_NotFromWhenThisWorkerStarted()
    {
        var history = Runs();

        var first = await Watch(history, new ListLogger(), startFresh: true).WatchAsync(CancellationToken.None);
        var afterARestart = await Watch(history, new ListLogger(), startFresh: true, now: Now.AddHours(2)).WatchAsync(CancellationToken.None);

        Assert.Equal(JobHealth.Healthy, first[PayloadArchiveCleanupFunction.JobName]);
        Assert.Equal(JobHealth.Overdue, afterARestart[PayloadArchiveCleanupFunction.JobName]);
    }

    [Fact]
    public async Task ARunThatNeverFinished_AndOneThatWasCancelled_EachSayWhatHappened()
    {
        var killed = new ListLogger();
        var cancelled = new ListLogger();

        await Watch(Runs(new LastRun(OnTheHour, null, OnTheHour.AddHours(-5))), killed).WatchAsync(CancellationToken.None);
        await Watch(Runs(new LastRun(OnTheHour, "Cancelled")), cancelled).WatchAsync(CancellationToken.None);

        Assert.Equal("Scheduled job payload-archive-cleanup is failing: a run started 2026-09-27 19:00:01Z never finished and none has finished since (it was killed or it hung)", Assert.Single(killed.Lines, line => line.Level == LogLevel.Error).Message);
        Assert.Equal("Scheduled job payload-archive-cleanup is failing: its last run, started 2026-09-28 00:00:01Z, was cancelled before it finished", Assert.Single(cancelled.Lines, line => line.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task AJobWithABadSchedule_IsThatJobsProblem_AndTheOthersAreStillJudged()
    {
        var logger = new ListLogger();
        var jobs = new[] { new WatchedJob("a-bad", "every hour"), new WatchedJob("b-literal", Hourly), new WatchedJob(PayloadArchiveCleanupFunction.JobName, "%PayloadCapture:CleanupCron%") };
        var history = new FixedHistory(new Dictionary<string, LastRun>
        {
            ["b-literal"] = new(OnTheHour, "Failed"),
            [PayloadArchiveCleanupFunction.JobName] = new(OnTheHour, "Succeeded"),
        });

        var health = await Watch(history, logger).WatchAsync(jobs, CancellationToken.None);

        Assert.Equal(JobHealth.Unwatchable, health["a-bad"]);
        Assert.Equal(JobHealth.Failing, health["b-literal"]);
        Assert.Equal(JobHealth.Healthy, health[PayloadArchiveCleanupFunction.JobName]);
        Assert.Equal(
        [
            (LogLevel.Error, "Scheduled job a-bad cannot be watched: its schedule 'every hour' is not a five- or six-field cron expression or an hh:mm:ss interval"),
            (LogLevel.Error, "Scheduled job b-literal is failing: its last run, started 2026-09-28 00:00:01Z, failed (the error is in job_runs)"),
            (LogLevel.Information, "Job watch: 3 scheduled jobs watched, 2 need attention"),
        ], logger.Lines);
    }

    [Fact]
    public async Task AHealthyPass_LogsOnlyTheWatchsOwnLine()
    {
        var logger = new ListLogger();

        await Watch(Runs(new LastRun(OnTheHour, "Succeeded")), logger).WatchAsync(CancellationToken.None);

        Assert.Equal([(LogLevel.Information, "Job watch: 1 scheduled jobs watched, 0 need attention")], logger.Lines);
    }

    [Fact]
    public async Task AJobWithNoScheduleConfigured_IsNotWatched()
    {
        var logger = new ListLogger();

        var health = await Watch(Runs(), logger, cron: null).WatchAsync(CancellationToken.None);

        Assert.Empty(health);
        Assert.Equal([(LogLevel.Information, "Job watch: 0 scheduled jobs watched, 0 need attention")], logger.Lines);
    }

    [Fact]
    public async Task WithoutRunHistory_TheWatchJudgesNothing()
    {
        var logger = new ListLogger();

        var health = await Watch(new NoJobRunHistory(), logger).WatchAsync(CancellationToken.None);

        Assert.Empty(health);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Lines).Level);
    }

    private static FixedHistory Runs(LastRun? cleanup = null)
        => new(cleanup is null ? new Dictionary<string, LastRun>() : new Dictionary<string, LastRun> { [PayloadArchiveCleanupFunction.JobName] = cleanup });

    private static JobWatchFunction Watch(IJobRunHistory history, ListLogger logger, string? cron = Hourly, bool startFresh = false, DateTimeOffset? now = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PayloadCapture:CleanupCron"] = cron })
            .Build();
        if (history is FixedHistory fixedHistory && !startFresh)
            fixedHistory.Since.TryAdd(PayloadArchiveCleanupFunction.JobName, LongAgo);
        return new JobWatchFunction(history, configuration, Options.Create(new JobWatchOptions()), new FixedClock(now ?? Now), logger);
    }

    private sealed class FixedHistory(IReadOnlyDictionary<string, LastRun> runs) : IJobRunHistory
    {
        public Dictionary<string, DateTimeOffset> Since { get; } = [];

        public Task<IReadOnlyDictionary<string, LastRun>?> LastRunsAsync(IReadOnlyList<string> jobNames, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, LastRun>?>(runs);

        public Task<IReadOnlyDictionary<string, DateTimeOffset>> WatchedSinceAsync(IReadOnlyList<string> jobNames, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            foreach (var name in jobNames)
                Since.TryAdd(name, nowUtc);
            return Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(Since);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ListLogger : ILogger<JobWatchFunction>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }
}

public static class SyntheticTimers
{
    [WatchedJob("synthetic-literal")]
    public static void Literal([Microsoft.Azure.Functions.Worker.TimerTrigger("0 0 * * * *")] object timer) => _ = timer;

    [WatchedJob("synthetic-setting")]
    public static void Setting([Microsoft.Azure.Functions.Worker.TimerTrigger("%Synthetic:Cron%")] object timer) => _ = timer;

    public static void Unwatched([Microsoft.Azure.Functions.Worker.TimerTrigger("00:05:00")] object timer) => _ = timer;
}
