# A scheduled job failed, or didn't run

The Functions worker runs the timer jobs (here, the payload archive cleanup on `PayloadCapture:CleanupCron`) and
each writes its runs to `job_runs`. Every 15 minutes (`JobWatch:Cron`) the job watch, `JobWatchFunction`, compares
each watched job's last run with its schedule and logs what it finds. A job is watched by carrying
`[WatchedJob("<name it records under>")]` on its timer function (`FunctionsHostConfigConventionTests` fails a timer
without one); its schedule is read from the setting its own `TimerTrigger("%Section:Key%")` names, so a derived
project's new timer job is watched as soon as it is declared and its schedule is configured. A job whose schedule
setting is empty is not watched. The outbox processor writes `job_runs` too, but only for windows with activity
and on no schedule, so it is not watched here; its `Degraded` rows are read with `scripts/reporting/job-run-history.sql`.

The watch writes three kinds of line, in fixed words (`JobWatchTests` pins them):

| Line | Level | What it means |
|---|---|---|
| `Scheduled job <name> is failing: its last run, started <time>, failed (the error is in job_runs)` | Error | The job's last finished run is `Failed`. Clears once a run succeeds. A `Degraded` run is not failing; the job logs its own Warning. |
| `Scheduled job <name> is overdue: it last started <time> and should have run again by <time>` | Error | The job's next scheduled start is more than `JobWatch:OverdueGraceMinutes` (15) past with no new start. A job that has never run is judged from when the watch started. |
| `Job watch: <n> scheduled jobs watched, <m> need attention` | Information | Every pass ends with it. Its absence means the worker, or its timers, or its logs are down. |

`host.json` excludes traces from sampling, so an Error line is never dropped before it reaches Application Insights.
The worker also counts every run on the `starterapp.scheduled_job.runs` counter (tagged `job` and `outcome`), which
the Aspire dashboard shows locally.

## Alert rules

The alert rules belong to the hosting environment's repository, not here. They are log queries over the lines
above; these are the three to declare (Application Insights `traces`, or `AppTraces` in the workspace):

```kusto
// A scheduled job failed: fire per job, every 15 minutes over the last 15 minutes
traces
| where message startswith "Scheduled job " and message contains " is failing: "
| extend Job = tostring(customDimensions.prop__Job)
| summarize count() by Job

// A scheduled job has not run: same shape
traces
| where message startswith "Scheduled job " and message contains " is overdue: "
| extend Job = tostring(customDimensions.prop__Job)
| summarize count() by Job

// The watch itself is silent: fire when this returns 0 over the last 30 minutes
traces
| where message startswith "Job watch: "
| count
```

The `Job` dimension's name depends on how the host forwards structured properties (`prop__Job` from the Functions
host, `Job` from the OpenTelemetry exporter); check one real line before declaring the rule. Change the wording of
the lines only together with the rules that match them.

## 1. Find out what happened

```sql
SELECT started_on_utc, completed_on_utc, outcome, summary
FROM job_runs WHERE job_name = 'payload-archive-cleanup'
ORDER BY started_on_utc DESC LIMIT 10;
```

A failed run's `summary` is `{"error": "..."}`. The job's own lines, and the host's, are in the same logs:

```kusto
traces
| where timestamp > ago(1d) and tostring(customDimensions.Category) startswith "Function."
| where severityLevel >= 2
| project timestamp, category = tostring(customDimensions.Category), message
| order by timestamp desc
```

## 2. Fix the cause

- **Failing**: the error in the summary says what threw (for the cleanup, usually the archive store). The next
  scheduled run tries again; the watch reports the job until a run succeeds.
- **Overdue**: check the schedule setting the worker actually has, and look for a run with no `completed_on_utc`
  long after it started: one that hung or whose process was killed.
- **The watch is silent**: is the Functions app running, and did the host index its functions (its console shows
  `Found the following functions`)? A missing `%Section:Key%` schedule setting fails indexing for every function in
  the worker, the Service Bus subscribers included.

## 3. Run it again

A run missed while the worker was down is made up when the host comes back, because the timer sees its last
scheduled start was missed. Otherwise the next scheduled run repeats the work; there is no button for an extra run,
so bring the schedule setting forward and put it back afterwards.
