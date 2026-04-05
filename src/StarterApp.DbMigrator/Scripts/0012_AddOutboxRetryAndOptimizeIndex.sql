-- Add RetryCount column for transient failure retry support.
-- Replace outbox polling index with a filtered index for optimal query performance.
-- Query: WHERE ProcessedOnUtc IS NULL AND Error IS NULL ORDER BY OccurredOnUtc

ALTER TABLE OutboxMessages ADD RetryCount INT NOT NULL CONSTRAINT DF_OutboxMessages_RetryCount DEFAULT 0;

-- Replace the old index with a filtered index.
-- Error is NVARCHAR(MAX) and cannot be a key column, but the query only checks IS NULL,
-- so a WHERE filter is both valid and more selective.
DROP INDEX IF EXISTS IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc ON OutboxMessages;

CREATE INDEX IX_OutboxMessages_Unprocessed
    ON OutboxMessages (OccurredOnUtc)
    WHERE ProcessedOnUtc IS NULL AND Error IS NULL;
