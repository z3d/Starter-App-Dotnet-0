CREATE TABLE inbox_messages (
    consumer varchar(100) NOT NULL,
    message_id varchar(128) NOT NULL,
    processed_on_utc timestamptz NOT NULL CONSTRAINT df_inbox_messages_processed_on_utc DEFAULT now(),
    CONSTRAINT pk_inbox_messages PRIMARY KEY (consumer, message_id)
);

CREATE INDEX ix_inbox_messages_processed_on_utc
    ON inbox_messages (processed_on_utc);
