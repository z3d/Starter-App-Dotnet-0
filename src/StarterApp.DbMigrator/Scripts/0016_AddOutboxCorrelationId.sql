ALTER TABLE OutboxMessages
    ADD CorrelationId NVARCHAR(128) NOT NULL
        CONSTRAINT DF_OutboxMessages_CorrelationId DEFAULT CONVERT(NVARCHAR(36), NEWID());
