-- migrator: no-transaction
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_idempotency_records_created_on_utc ON idempotency_records (created_on_utc);
