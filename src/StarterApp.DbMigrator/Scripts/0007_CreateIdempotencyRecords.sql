CREATE TABLE idempotency_records (
    tenant_id varchar(100) NOT NULL,
    owner_subject varchar(200) NOT NULL,
    operation varchar(100) NOT NULL,
    idempotency_key varchar(255) NOT NULL,
    request_hash varchar(64) NOT NULL,
    resource_id varchar(100) NOT NULL,
    created_on_utc timestamptz NOT NULL CONSTRAINT df_idempotency_records_created_on_utc DEFAULT now(),
    CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, owner_subject, operation, idempotency_key)
);
