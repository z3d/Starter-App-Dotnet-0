-- Errored outbox rows are retained for RetentionDays from the moment they became permanently
-- errored, not from the event's OccurredOnUtc: an event that stayed pending through an outage longer
-- than the retention window used to be purged by the same processor loop that had just marked it
-- errored, leaving no replay window. Existing errored rows are stamped now() so each gets a full
-- window from this migration.
ALTER TABLE outbox_messages
    ADD COLUMN errored_on_utc timestamptz NULL;

UPDATE outbox_messages
SET errored_on_utc = now()
WHERE error IS NOT NULL AND errored_on_utc IS NULL;
