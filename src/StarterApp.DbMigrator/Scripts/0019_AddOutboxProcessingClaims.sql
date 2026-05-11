ALTER TABLE OutboxMessages
    ADD ProcessingId UNIQUEIDENTIFIER NULL,
        LockedUntilUtc DATETIMEOFFSET NULL;

DROP INDEX IF EXISTS IX_OutboxMessages_Unprocessed ON OutboxMessages;

CREATE INDEX IX_OutboxMessages_Claimable
    ON OutboxMessages (OccurredOnUtc, LockedUntilUtc)
    WHERE ProcessedOnUtc IS NULL AND Error IS NULL;
