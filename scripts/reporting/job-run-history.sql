-- Background-work run history (job_runs is written by the outbox processor's aggregate
-- health windows and the payload-archive cleanup function's per-run records).

-- Most recent runs per job
SELECT job_name, started_on_utc, completed_on_utc, outcome, summary
FROM job_runs
ORDER BY started_on_utc DESC
LIMIT 50;

-- Outcome distribution per job over the last 7 days (Failed/Degraded rows are the signal)
SELECT job_name, outcome, count(*) AS runs
FROM job_runs
WHERE started_on_utc >= now() - interval '7 days'
GROUP BY job_name, outcome
ORDER BY job_name, outcome;

-- Runs that never completed (job crashed between start and complete records)
SELECT job_name, started_on_utc
FROM job_runs
WHERE completed_on_utc IS NULL
  AND started_on_utc < now() - interval '1 hour'
ORDER BY started_on_utc;
