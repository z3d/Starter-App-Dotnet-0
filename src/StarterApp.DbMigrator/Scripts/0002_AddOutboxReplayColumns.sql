ALTER TABLE outbox_messages
    ADD COLUMN replay_count integer NOT NULL CONSTRAINT df_outbox_messages_replay_count DEFAULT 0,
    ADD COLUMN replayed_on_utc timestamptz NULL;
