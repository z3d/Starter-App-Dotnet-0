-- Outbox health: pending backlog, errored rows, replay trail, and daily publish volume.
-- Daily volume doubles as a proxy for outbound Service Bus payload-capture volume:
-- every published row was captured to the archive before publish.

-- Pending vs errored right now (paused-unprocessed vs needs-replay; see event-replay runbook)
SELECT count(*) FILTER (WHERE error IS NULL)     AS pending,
       count(*) FILTER (WHERE error IS NOT NULL) AS errored
FROM outbox_messages
WHERE processed_on_utc IS NULL;

-- Errored rows in full (the replay-decision surface)
SELECT id, type, correlation_id, occurred_on_utc, retry_count, replay_count, replayed_on_utc, error
FROM outbox_messages
WHERE error IS NOT NULL AND processed_on_utc IS NULL
ORDER BY occurred_on_utc;

-- Replay audit trail: operator-replayed rows and whether they subsequently processed
SELECT id, type, replay_count, replayed_on_utc, processed_on_utc, error
FROM outbox_messages
WHERE replay_count > 0
ORDER BY replayed_on_utc DESC;

-- Published volume per day per event type (last 30 days)
SELECT date_trunc('day', processed_on_utc) AS day, type, count(*) AS published
FROM outbox_messages
WHERE processed_on_utc >= now() - interval '30 days'
GROUP BY 1, 2
ORDER BY 1 DESC, 2;
