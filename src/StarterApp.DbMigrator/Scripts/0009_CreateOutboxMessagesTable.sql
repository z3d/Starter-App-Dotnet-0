-- Create the OutboxMessages table for durable domain event persistence
CREATE TABLE OutboxMessages (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    OccurredOnUtc DATETIME2 NOT NULL,
    Type NVARCHAR(200) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    ProcessedOnUtc DATETIME2 NULL,
    Error NVARCHAR(MAX) NULL
);

-- Support polling unprocessed messages oldest-first
CREATE INDEX IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc
    ON OutboxMessages (ProcessedOnUtc, OccurredOnUtc);
